using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace GrampsWeb.Mcp.Serialization;

/// <summary>
/// Shared <see cref="JsonSerializerOptions"/> for Gramps Web API requests and responses.
/// Unit tests use <see cref="Options"/> via <c>InternalsVisibleTo</c> (see also <c>docs/API_INVENTORY.md</c>).
/// </summary>
public static class GrampsJson
{
    /// <summary>Serializer settings aligned with <see cref="Client.GrampsApiClient"/>.</summary>
    internal static readonly JsonSerializerOptions Options = CreateOptions();

    internal static JsonSerializerOptions CreateOptions()
    {
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(static typeInfo =>
        {
            if (typeInfo.Kind != JsonTypeInfoKind.Object)
            {
                return;
            }

            foreach (var property in typeInfo.Properties)
            {
                if (property.PropertyType == typeof(string))
                {
                    continue;
                }

                if (!typeof(System.Collections.IEnumerable).IsAssignableFrom(property.PropertyType))
                {
                    continue;
                }

                property.ShouldSerialize = static (_, value) =>
                {
                    if (value is null)
                    {
                        return false;
                    }

                    return value switch
                    {
                        Array array => array.Length > 0,
                        System.Collections.ICollection collection => collection.Count > 0,
                        _ => true
                    };
                };
            }
        });

        return new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            TypeInfoResolver = resolver
        };
    }
}
