using System.Text.Json.Serialization;

namespace GrampsWeb.Mcp.Models;

/// <summary>
/// Represents a tag entity from the Gramps genealogy database.
/// </summary>
public class GrampsTag
{
    [JsonPropertyName("handle")]
    public string? Handle { get; set; }

    [JsonPropertyName("gramps_id")]
    public string? GrampsId { get; set; }

    [JsonPropertyName("change")]
    public long? Change { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("color")]
    public string? Color { get; set; }

    [JsonPropertyName("priority")]
    public int Priority { get; set; }
}
