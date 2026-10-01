using System.Text.Json;
using YamlDotNet.RepresentationModel;

namespace GrampsWeb.Mcp.Tests.Contract;

/// <summary>Indexes the current OpenAPI 3 schemas, with four legacy extended schemas from Swagger 2.</summary>
public sealed class SwaggerDefinitionsIndex
{
    private readonly JsonDocument _openApi;
    private readonly Dictionary<string, YamlMappingNode> _legacyDefinitions;

    public SwaggerDefinitionsIndex(string openApiPath, string legacyYamlPath)
    {
        _openApi = JsonDocument.Parse(File.ReadAllText(openApiPath));
        using var reader = new StreamReader(legacyYamlPath);
        var yaml = new YamlStream();
        yaml.Load(reader);
        var root = (YamlMappingNode)yaml.Documents[0].RootNode!;
        var definitions = (YamlMappingNode)root.Children[new YamlScalarNode("definitions")];
        _legacyDefinitions = definitions.Children
            .Where(pair => pair.Key is YamlScalarNode && pair.Value is YamlMappingNode)
            .ToDictionary(pair => ((YamlScalarNode)pair.Key).Value!, pair => (YamlMappingNode)pair.Value, StringComparer.Ordinal);
    }

    private bool TryGetCurrentSchema(string name, out JsonElement schema) =>
        _openApi.RootElement.GetProperty("components").GetProperty("schemas").TryGetProperty(name, out schema);

    private YamlMappingNode GetLegacySchema(string name)
    {
        if (!name.EndsWith("Extended", StringComparison.Ordinal) || !_legacyDefinitions.TryGetValue(name, out var schema))
            throw new ArgumentException($"Unknown OpenAPI schema '{name}'.", nameof(name));
        return schema;
    }

    public IReadOnlySet<string> GetPropertyKeys(string definitionName)
    {
        if (TryGetCurrentSchema(definitionName, out var schema))
            return schema.TryGetProperty("properties", out var properties)
                ? properties.EnumerateObject().Select(property => property.Name).ToHashSet(StringComparer.Ordinal)
                : new HashSet<string>(StringComparer.Ordinal);
        var legacy = GetLegacySchema(definitionName);
        return legacy.Children.TryGetValue(new YamlScalarNode("properties"), out var node)
            ? ((YamlMappingNode)node).Children.Keys.OfType<YamlScalarNode>().Select(key => key.Value!).ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);
    }

    public string? TryGetReferencedDefinitionForProperty(string definitionName, string jsonPropertyName)
    {
        if (TryGetCurrentSchema(definitionName, out var schema))
        {
            if (!schema.TryGetProperty("properties", out var properties) || !properties.TryGetProperty(jsonPropertyName, out var property))
                return null;
            return FindCurrentRef(property);
        }
        var legacy = GetLegacySchema(definitionName);
        if (!legacy.Children.TryGetValue(new YamlScalarNode("properties"), out var propertiesNode) ||
            !((YamlMappingNode)propertiesNode).Children.TryGetValue(new YamlScalarNode(jsonPropertyName), out var propertyNode))
            return null;
        return FindLegacyRef(propertyNode);
    }

    private static string? FindCurrentRef(JsonElement node)
    {
        if (node.ValueKind != JsonValueKind.Object)
            return null;
        if (node.TryGetProperty("$ref", out var reference))
        {
            const string prefix = "#/components/schemas/";
            var value = reference.GetString();
            if (value?.StartsWith(prefix, StringComparison.Ordinal) == true)
                return value[prefix.Length..];
        }
        if (node.TryGetProperty("items", out var items))
            return FindCurrentRef(items);
        if (node.TryGetProperty("allOf", out var allOf))
            foreach (var child in allOf.EnumerateArray())
                if (FindCurrentRef(child) is { } result)
                    return result;
        return null;
    }

    private static string? FindLegacyRef(YamlNode node)
    {
        if (node is not YamlMappingNode map)
            return null;
        const string prefix = "#/definitions/";
        if (map.Children.TryGetValue(new YamlScalarNode("$ref"), out var reference) &&
            reference is YamlScalarNode scalar && scalar.Value?.StartsWith(prefix, StringComparison.Ordinal) == true)
            return scalar.Value[prefix.Length..];
        return map.Children.TryGetValue(new YamlScalarNode("items"), out var items) ? FindLegacyRef(items) : null;
    }
}
