using System.Text.Json.Serialization;
using GrampsWeb.Mcp.Serialization;

namespace GrampsWeb.Mcp.Models;

/// <summary>
/// Gramps Web API <c>PlaceName</c> (primary or alternate place name).
/// </summary>
[JsonConverter(typeof(GrampsPlaceNameJsonConverter))]
public sealed class GrampsPlaceName
{
    [JsonPropertyName("value")]
    public string? Value { get; set; }

    [JsonPropertyName("lang")]
    public string? Lang { get; set; }

    [JsonPropertyName("date")]
    public GrampsDate? Date { get; set; }
}
