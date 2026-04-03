using System.Text.Json.Serialization;

namespace GrampsWeb.Mcp.Requests;

/// <summary>
/// Gramps <c>StyledText</c> shape for note (and similar) write requests.
/// </summary>
public class StyledTextRequest
{
    [JsonPropertyName("_class")]
    public string Class { get; set; } = "StyledText";

    [JsonPropertyName("string")]
    public string? Text { get; set; }

    [JsonPropertyName("tags")]
    public object[] Tags { get; set; } = [];
}
