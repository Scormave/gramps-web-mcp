using System.Text.Json;
using System.Text.Json.Serialization;

namespace GrampsWeb.Mcp.Models;

/// <summary>
/// Represents a single search result hit from the Gramps API.
/// </summary>
public class GrampsSearchHit
{
    [JsonPropertyName("handle")]
    public string? Handle { get; set; }

    [JsonPropertyName("object_type")]
    public string? ObjectType { get; set; }

    [JsonPropertyName("gramps_id")]
    public string? GrampsId { get; set; }

    [JsonPropertyName("rank")]
    public double Rank { get; set; }

    /// <summary>Relevance score; some responses use <c>rank</c> instead.</summary>
    [JsonPropertyName("score")]
    public double Score { get; set; }

    [JsonPropertyName("object")]
    public JsonElement? Object { get; set; }
}
