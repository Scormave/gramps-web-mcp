using System.Text.Json.Serialization;

namespace GrampsWeb.Mcp.Models;

/// <summary>
/// One <c>media_list</c> entry from Gramps GET payloads (Gramps <c>MediaRef</c>).
/// </summary>
public sealed class GrampsMediaRef
{
    [JsonPropertyName("ref")]
    public string? Ref { get; set; }

    /// <summary>Non-empty <c>ref</c> after normalisation (some responses set only <c>handle</c>, copied into <see cref="Ref"/> when deserializing).</summary>
    public string? ResolvedRef => string.IsNullOrWhiteSpace(Ref) ? null : Ref.Trim();

    [JsonPropertyName("private")]
    public bool Private { get; set; }

    [JsonPropertyName("citation_list")]
    public string[]? CitationList { get; set; }

    [JsonPropertyName("note_list")]
    public string[]? NoteList { get; set; }

    [JsonPropertyName("attribute_list")]
    public object[]? AttributeList { get; set; }

    /// <summary>Image crop rectangle <c>[x, y, width, height]</c>.</summary>
    [JsonPropertyName("rect")]
    public int[]? Rect { get; set; }

    /// <summary>Handles from <paramref name="list"/> for display and media fetch (non-empty <c>ref</c> values only).</summary>
    public static string[]? ToHandleStrings(GrampsMediaRef[]? list)
    {
        if (list is null || list.Length == 0)
            return null;
        var hs = list
            .Select(m => m.ResolvedRef)
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(r => r!)
            .ToArray();
        return hs.Length == 0 ? null : hs;
    }
}
