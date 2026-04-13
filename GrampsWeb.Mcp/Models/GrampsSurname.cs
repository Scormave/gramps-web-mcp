using System.Text.Json.Serialization;
using GrampsWeb.Mcp.Serialization;

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

    /// <summary>Gramps surname origin type (JSON key <c>origintype</c>). May arrive as string or wire type object.</summary>
    [JsonPropertyName("origintype")]
    [JsonConverter(typeof(GrampsWireTypeStringConverter))]
    public string? OriginType { get; set; }

    [JsonPropertyName("primary")]
    public bool Primary { get; set; } = true;
}
