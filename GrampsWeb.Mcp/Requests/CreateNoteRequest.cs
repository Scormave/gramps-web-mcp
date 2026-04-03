using System.Text.Json.Serialization;

namespace GrampsWeb.Mcp.Requests;

/// <summary>
/// Request body for creating or updating a note (matches Gramps Web schema: <c>_class</c>, <c>text</c> as <see cref="StyledTextRequest"/>).
/// </summary>
public class CreateNoteRequest
{
    [JsonPropertyName("_class")]
    public string Class { get; set; } = "Note";

    /// <summary>Omit on POST create; required on PUT for Gramps Web.</summary>
    [JsonPropertyName("handle")]
    public string? Handle { get; set; }

    /// <summary>Omit on POST create; include on PUT when known.</summary>
    [JsonPropertyName("gramps_id")]
    public string? GrampsId { get; set; }

    /// <summary>Last-modified epoch from GET; include on PUT so the server accepts the update.</summary>
    [JsonPropertyName("change")]
    public long? Change { get; set; }

    [JsonPropertyName("text")]
    public StyledTextRequest? Text { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("format")]
    public int Format { get; set; } = 0; // 0=plain text, 1=flowed

    [JsonPropertyName("tag_list")]
    public string[]? TagList { get; set; }

    /// <summary>When null, omitted on POST create (server default). Set on PUT from current note.</summary>
    [JsonPropertyName("private")]
    public bool? Private { get; set; }
}
