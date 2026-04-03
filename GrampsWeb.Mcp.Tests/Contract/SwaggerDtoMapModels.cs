using System.Text.Json.Serialization;

namespace GrampsWeb.Mcp.Tests.Contract;

public sealed class SwaggerDtoMapFile
{
    [JsonPropertyName("entries")]
    public List<SwaggerDtoMapEntry> Entries { get; set; } = [];
}

public sealed class SwaggerDtoMapEntry
{
    [JsonPropertyName("typeName")]
    public string TypeName { get; set; } = "";

    [JsonPropertyName("swaggerDefinition")]
    public string? SwaggerDefinition { get; set; }

    /// <summary>When set, every <see cref="JsonPropertyNameAttribute"/> on the type must appear in this list (plus <see cref="AllowedExtraJsonNames"/>).</summary>
    [JsonPropertyName("onlyTheseJsonNames")]
    public string[]? OnlyTheseJsonNames { get; set; }

    [JsonPropertyName("allowedExtraJsonNames")]
    public string[]? AllowedExtraJsonNames { get; set; }

    [JsonPropertyName("skipNested")]
    public bool SkipNested { get; set; }
}
