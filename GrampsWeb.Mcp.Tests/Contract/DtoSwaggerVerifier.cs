using System.Collections;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GrampsWeb.Mcp.Tests.Contract;

public static class DtoSwaggerVerifier
{
    private const int MaxNestedDepth = 16;

    public static IReadOnlyList<string> VerifyAll(string apispecPath, string mapPath)
    {
        var json = File.ReadAllText(mapPath);
        var map = System.Text.Json.JsonSerializer.Deserialize<SwaggerDtoMapFile>(json);
        if (map?.Entries is not { Count: > 0 })
            return ["swagger-dto-map.json has no entries."];

        var index = new SwaggerDefinitionsIndex(apispecPath);
        var errors = new List<string>();
        var mcpAssembly = typeof(GrampsWeb.Mcp.Models.GrampsPerson).Assembly;

        foreach (var entry in map.Entries)
        {
            var type = ResolveMapType(mcpAssembly, entry.TypeName);
            if (type == null)
            {
                errors.Add($"Map entry: could not resolve CLR type '{entry.TypeName}'.");
                continue;
            }

            errors.AddRange(VerifyMappedType(index, map.Entries, mcpAssembly, entry, type));
        }

        return errors.Distinct(StringComparer.Ordinal).OrderBy(s => s, StringComparer.Ordinal).ToList();
    }

    private static Type? ResolveMapType(Assembly assembly, string typeNameEntry)
    {
        var shortName = typeNameEntry.Contains(',', StringComparison.Ordinal)
            ? typeNameEntry[..typeNameEntry.IndexOf(',', StringComparison.Ordinal)].Trim()
            : typeNameEntry.Trim();
        return assembly.GetType(shortName) ?? Type.GetType(typeNameEntry, throwOnError: false);
    }

    /// <summary>
    /// Finds the map row for this CLR type + Swagger definition (skips <see cref="SwaggerDtoMapEntry.OnlyTheseJsonNames"/> rows).
    /// Prefers exact type match; otherwise the most derived registered superclass.
    /// </summary>
    private static SwaggerDtoMapEntry? FindEntryForDefinition(
        IReadOnlyList<SwaggerDtoMapEntry> entries,
        Assembly assembly,
        Type clrType,
        string swaggerDefinition)
    {
        SwaggerDtoMapEntry? subclassMatch = null;
        foreach (var e in entries)
        {
            if (e.OnlyTheseJsonNames is { Length: > 0 })
                continue;
            if (!string.Equals(e.SwaggerDefinition, swaggerDefinition, StringComparison.Ordinal))
                continue;
            var resolved = ResolveMapType(assembly, e.TypeName);
            if (resolved == null)
                continue;
            if (resolved == clrType)
                return e;
            if (clrType.IsSubclassOf(resolved))
                subclassMatch = e;
        }

        return subclassMatch;
    }

    private static IEnumerable<string> VerifyMappedType(
        SwaggerDefinitionsIndex index,
        IReadOnlyList<SwaggerDtoMapEntry> mapEntries,
        Assembly assembly,
        SwaggerDtoMapEntry entry,
        Type clrType)
    {
        if (entry.OnlyTheseJsonNames is { Length: > 0 } allowList)
        {
            var allowed = new HashSet<string>(allowList, StringComparer.Ordinal);
            if (entry.AllowedExtraJsonNames is { Length: > 0 })
                foreach (var x in entry.AllowedExtraJsonNames)
                    allowed.Add(x);

            foreach (var (jsonName, prop) in GetJsonMappedProperties(clrType))
            {
                if (!allowed.Contains(jsonName))
                    yield return $"{clrType.Name}: JSON name '{jsonName}' (property {prop.Name}) is not in onlyTheseJsonNames / allowedExtraJsonNames for this map entry.";
            }

            yield break;
        }

        if (string.IsNullOrEmpty(entry.SwaggerDefinition))
        {
            yield return $"{clrType.Name}: map entry must set swaggerDefinition or onlyTheseJsonNames.";
            yield break;
        }

        var visited = new HashSet<(Type Type, string Def)>();
        foreach (var err in VerifyAgainstDefinition(
                     index,
                     mapEntries,
                     assembly,
                     clrType,
                     entry.SwaggerDefinition,
                     entry,
                     visited,
                     depth: 0))
            yield return err;
    }

    private static IEnumerable<string> VerifyAgainstDefinition(
        SwaggerDefinitionsIndex index,
        IReadOnlyList<SwaggerDtoMapEntry> mapEntries,
        Assembly assembly,
        Type clrType,
        string swaggerDefinition,
        SwaggerDtoMapEntry selfEntry,
        HashSet<(Type Type, string Def)> visited,
        int depth)
    {
        if (depth > MaxNestedDepth)
        {
            yield return $"{clrType.Name}: nested verification exceeded max depth ({MaxNestedDepth}) at definition '{swaggerDefinition}'.";
            yield break;
        }

        string? definitionLookupError = null;
        HashSet<string> specKeys;
        try
        {
            specKeys = index.GetPropertyKeys(swaggerDefinition).ToHashSet(StringComparer.Ordinal);
        }
        catch (ArgumentException ex)
        {
            definitionLookupError = ex.Message;
            specKeys = [];
        }

        if (definitionLookupError is not null)
        {
            yield return $"{clrType.Name}: {definitionLookupError}";
            yield break;
        }

        var allowedExtra = new HashSet<string>(StringComparer.Ordinal);
        if (selfEntry.AllowedExtraJsonNames is { Length: > 0 })
            foreach (var x in selfEntry.AllowedExtraJsonNames)
                allowedExtra.Add(x);

        foreach (var (jsonName, prop) in GetJsonMappedProperties(clrType))
        {
            if (!specKeys.Contains(jsonName) && !allowedExtra.Contains(jsonName))
                yield return $"{clrType.Name}: JSON name '{jsonName}' (property {prop.Name}) is not a property of Swagger definition '{swaggerDefinition}'.";

            if (selfEntry.SkipNested)
                continue;

            var refDef = index.TryGetReferencedDefinitionForProperty(swaggerDefinition, jsonName);
            if (refDef == null)
                continue;

            if (!TryGetNestedClrType(prop.PropertyType, out var nestedClr))
                continue;

            if (IsLeafOrAmbiguous(nestedClr))
                continue;

            var pair = (nestedClr, refDef);
            if (!visited.Add(pair))
                continue;

            var nestedSelf = FindEntryForDefinition(mapEntries, assembly, nestedClr, refDef)
                             ?? new SwaggerDtoMapEntry { SwaggerDefinition = refDef };

            foreach (var err in VerifyAgainstDefinition(
                         index,
                         mapEntries,
                         assembly,
                         nestedClr,
                         refDef,
                         nestedSelf,
                         visited,
                         depth + 1))
                yield return err;
        }
    }

    private static IEnumerable<(string JsonName, PropertyInfo Prop)> GetJsonMappedProperties(Type type)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public;
        foreach (var prop in type.GetProperties(flags))
        {
            if (prop.GetIndexParameters().Length > 0)
                continue;
            if (prop.GetCustomAttribute<JsonIgnoreAttribute>() is not null)
                continue;
            var jn = prop.GetCustomAttribute<JsonPropertyNameAttribute>();
            if (jn?.Name is not { } jsonName)
                continue;
            yield return (jsonName, prop);
        }
    }

    private static bool TryGetNestedClrType(Type propertyType, out Type nestedType)
    {
        nestedType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;
        if (nestedType.IsArray)
        {
            nestedType = nestedType.GetElementType()!;
            return true;
        }

        if (nestedType == typeof(string))
            return false;

        if (nestedType.IsGenericType)
        {
            var def = nestedType.GetGenericTypeDefinition();
            if (def == typeof(List<>) || def == typeof(IList<>) || def == typeof(ICollection<>))
            {
                nestedType = nestedType.GetGenericArguments()[0];
                return true;
            }
        }

        if (typeof(IEnumerable).IsAssignableFrom(nestedType) && nestedType != typeof(string))
        {
            var iface = nestedType.GetInterfaces()
                .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));
            if (iface != null)
            {
                nestedType = iface.GetGenericArguments()[0];
                return true;
            }
        }

        return !IsLeafOrAmbiguous(nestedType);
    }

    private static bool IsLeafOrAmbiguous(Type t)
    {
        t = Nullable.GetUnderlyingType(t) ?? t;
        if (t.IsPrimitive || t.IsEnum || t == typeof(string) || t == typeof(decimal))
            return true;
        if (t == typeof(object) || t == typeof(JsonElement) || t == typeof(JsonDocument))
            return true;
        return false;
    }
}
