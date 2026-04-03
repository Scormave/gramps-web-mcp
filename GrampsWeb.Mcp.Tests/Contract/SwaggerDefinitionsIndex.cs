using YamlDotNet.RepresentationModel;

namespace GrampsWeb.Mcp.Tests.Contract;

/// <summary>
/// Loads Swagger 2 <c>definitions</c> from <c>apispec.yaml</c> for structural DTO checks.
/// </summary>
public sealed class SwaggerDefinitionsIndex
{
    private const string DefinitionsRefPrefix = "#/definitions/";

    private readonly Dictionary<string, YamlMappingNode> _definitions;

    public SwaggerDefinitionsIndex(string yamlPath)
    {
        using var reader = new StreamReader(yamlPath);
        var yaml = new YamlStream();
        yaml.Load(reader);
        var root = (YamlMappingNode)yaml.Documents[0].RootNode!;
        if (!root.Children.TryGetValue(new YamlScalarNode("definitions"), out var defsRoot))
            throw new InvalidOperationException("YAML root has no 'definitions' mapping (expected Swagger 2).");
        var defs = (YamlMappingNode)defsRoot;
        _definitions = new Dictionary<string, YamlMappingNode>(StringComparer.Ordinal);
        foreach (var (k, v) in defs.Children)
        {
            if (k is YamlScalarNode sk && v is YamlMappingNode mv && sk.Value is { } name)
                _definitions[name] = mv;
        }
    }

    public IReadOnlySet<string> GetPropertyKeys(string definitionName)
    {
        if (!_definitions.TryGetValue(definitionName, out var schema))
            throw new ArgumentException($"Unknown Swagger definition '{definitionName}'.", nameof(definitionName));
        if (!schema.Children.TryGetValue(new YamlScalarNode("properties"), out var propsNode))
            return new HashSet<string>(StringComparer.Ordinal);
        var props = (YamlMappingNode)propsNode;
        return props.Children.Keys
            .OfType<YamlScalarNode>()
            .Where(s => s.Value is not null)
            .Select(s => s.Value!)
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// If the schema for <paramref name="jsonPropertyName"/> eventually contains <c>$ref: #/definitions/X</c>
    /// (including under <c>items</c>), returns <c>X</c>; otherwise <c>null</c>.
    /// </summary>
    public string? TryGetReferencedDefinitionForProperty(string definitionName, string jsonPropertyName)
    {
        if (!_definitions.TryGetValue(definitionName, out var schema))
            return null;
        if (!schema.Children.TryGetValue(new YamlScalarNode("properties"), out var propsNode))
            return null;
        var props = (YamlMappingNode)propsNode;
        if (!props.Children.TryGetValue(new YamlScalarNode(jsonPropertyName), out var propSchema))
            return null;
        return ExtractRefDefinitionName(propSchema);
    }

    private static string? ExtractRefDefinitionName(YamlNode? node)
    {
        if (node is not YamlMappingNode map)
            return null;
        if (map.Children.TryGetValue(new YamlScalarNode("$ref"), out var refN) && refN is YamlScalarNode rs)
            return ParseDefinitionsRef(rs.Value);
        if (map.Children.TryGetValue(new YamlScalarNode("items"), out var items))
            return ExtractRefDefinitionName(items);
        return null;
    }

    private static string? ParseDefinitionsRef(string? refValue)
    {
        if (string.IsNullOrEmpty(refValue))
            return null;
        return refValue.StartsWith(DefinitionsRefPrefix, StringComparison.Ordinal)
            ? refValue[DefinitionsRefPrefix.Length..]
            : null;
    }
}
