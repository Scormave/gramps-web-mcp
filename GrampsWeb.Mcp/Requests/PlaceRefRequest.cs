using System.Text.Json.Serialization;

namespace GrampsWeb.Mcp.Requests;

/// <summary>
/// Gramps Web API <c>PlaceReference</c> shape for POST/PUT.
/// </summary>
public sealed class PlaceRefRequest
{
    [JsonPropertyName("ref")]
    public string? Ref { get; set; }

    [JsonPropertyName("date")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateRequest? Date { get; set; }
}
