using System.Text.Json.Serialization;

namespace GrampsWeb.Mcp.Requests;

/// <summary>
/// Gramps Web API <c>Place.name</c> shape (see apispec <c>PlaceName</c>). Plain strings are rejected on POST/PUT.
/// </summary>
public sealed class PlaceNameRequest
{
    [JsonPropertyName("value")]
    public string Value { get; set; } = "";

    [JsonPropertyName("lang")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Lang { get; set; }
}
