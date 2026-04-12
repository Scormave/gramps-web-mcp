using System.Text.Json.Serialization;

namespace GrampsWeb.Mcp.Models;

/// <summary>Web or other URL entry on a Person (Gramps Web <c>urls</c> items).</summary>
public class GrampsUrl
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("path")]
    public string? Path { get; set; }

    [JsonPropertyName("desc")]
    public string? Description { get; set; }

    [JsonPropertyName("private")]
    public bool Private { get; set; }
}
