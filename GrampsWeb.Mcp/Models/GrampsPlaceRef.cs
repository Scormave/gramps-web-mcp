using System.Text.Json.Serialization;
using GrampsWeb.Mcp.Serialization;

namespace GrampsWeb.Mcp.Models;

/// <summary>
/// Gramps Web API <c>PlaceReference</c> (<c>placeref_list</c> item).
/// </summary>
[JsonConverter(typeof(GrampsPlaceRefJsonConverter))]
public sealed class GrampsPlaceRef
{
    [JsonPropertyName("ref")]
    public string? Ref { get; set; }

    [JsonPropertyName("date")]
    public GrampsDate? Date { get; set; }
}
