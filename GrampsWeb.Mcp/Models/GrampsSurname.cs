using System.Text.Json.Serialization;

namespace GrampsWeb.Mcp.Models;

/// <summary>
/// Represents a surname within a Name object, supporting multiple surnames per person.
/// </summary>
public class GrampsSurname
{
    [JsonPropertyName("surname")]
    public string? Surname { get; set; }

    [JsonPropertyName("prefix")]
    public string? Prefix { get; set; }

    [JsonPropertyName("connector")]
    public string? Connector { get; set; }

    [JsonPropertyName("origintype")]
    public string? OriginType { get; set; }

    [JsonPropertyName("primary")]
    public bool Primary { get; set; } = true;
}
