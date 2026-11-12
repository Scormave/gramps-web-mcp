using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace GrampsWeb.Mcp.Formatters;

/// <summary>
/// Adds machine-readable metadata headers and next-step hints to tool responses.
/// </summary>
public static class ResponseEnvelope
{
    /// <summary>Quotes header text as JSON, leaving letters such as Cyrillic unescaped.</summary>
    private static readonly JsonSerializerOptions HeaderJson = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>
    /// Formats a successful create response with next steps. <paramref name="stored"/> adds header lines
    /// with values the record now holds (see <see cref="HeaderValue"/>).
    /// </summary>
    public static string CreateSuccess(string objectType, string? handle, string? grampsId, string? displayName,
        string[]? nextSteps = null, IEnumerable<KeyValuePair<string, string>>? stored = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine("---");
        sb.AppendLine($"type: {objectType}");
        sb.AppendLine($"action: created");
        if (!string.IsNullOrWhiteSpace(handle))
            sb.AppendLine($"handle: {handle}");
        if (!string.IsNullOrWhiteSpace(grampsId))
            sb.AppendLine($"gramps_id: {grampsId}");
        if (!string.IsNullOrWhiteSpace(displayName))
            sb.AppendLine($"name: {HeaderValue(displayName)}");
        AppendStored(sb, stored);
        sb.AppendLine("---");

        if (nextSteps is { Length: > 0 })
        {
            sb.AppendLine();
            sb.AppendLine("Next steps:");
            foreach (var step in nextSteps)
                sb.AppendLine($"  • {step}");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Formats the response of upload_media when it found the file already stored and created nothing:
    /// <c>action: existing</c>, then <paramref name="message"/> saying what was and was not applied.
    /// </summary>
    public static string FoundExisting(string objectType, string? handle, string? grampsId, string? displayName,
        string message, IEnumerable<KeyValuePair<string, string>>? stored = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine("---");
        sb.AppendLine($"type: {objectType}");
        sb.AppendLine($"action: existing");
        if (!string.IsNullOrWhiteSpace(handle))
            sb.AppendLine($"handle: {handle}");
        if (!string.IsNullOrWhiteSpace(grampsId))
            sb.AppendLine($"gramps_id: {grampsId}");
        if (!string.IsNullOrWhiteSpace(displayName))
            sb.AppendLine($"name: {HeaderValue(displayName)}");
        AppendStored(sb, stored);
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine(message);
        return sb.ToString();
    }

    /// <summary>
    /// Formats a successful update response. <paramref name="stored"/> adds header lines with values the
    /// record now holds (see <see cref="HeaderValue"/>).
    /// </summary>
    public static string UpdateSuccess(string objectType, string? handle, string? grampsId,
        IEnumerable<KeyValuePair<string, string>>? stored = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine("---");
        sb.AppendLine($"type: {objectType}");
        sb.AppendLine($"action: updated");
        if (!string.IsNullOrWhiteSpace(handle))
            sb.AppendLine($"handle: {handle}");
        if (!string.IsNullOrWhiteSpace(grampsId))
            sb.AppendLine($"gramps_id: {grampsId}");
        AppendStored(sb, stored);
        sb.AppendLine("---");
        return sb.ToString();
    }

    /// <summary>
    /// A header value: the text in JSON quotes, or <c>none</c> when it is empty. Clients drop an argument a tool
    /// does not have before calling it, so <c>latitude</c> instead of <c>lat</c> never reaches the server; showing
    /// what the record holds makes such a dropped argument visible as a value that did not change.
    /// </summary>
    public static string HeaderValue(string? text) =>
        string.IsNullOrWhiteSpace(text) ? "none" : JsonSerializer.Serialize(text, HeaderJson);

    /// <summary>A header list: the texts as a JSON array, or <c>none</c> when there are none.</summary>
    public static string HeaderValue(IEnumerable<string?>? texts) =>
        texts?.Where(t => !string.IsNullOrWhiteSpace(t)).ToArray() is { Length: > 0 } list
            ? JsonSerializer.Serialize(list, HeaderJson)
            : "none";

    private static void AppendStored(StringBuilder sb, IEnumerable<KeyValuePair<string, string>>? stored)
    {
        foreach (var (key, value) in stored ?? [])
            sb.AppendLine($"{key}: {value}");
    }

    /// <summary>
    /// Formats a merge that did not happen because the user did not confirm it, Gramps refused it, or a record
    /// changed meanwhile: <paramref name="action"/> such as <c>declined</c> or <c>refused</c>, then why.
    /// </summary>
    public static string MergeNotDone(string objectType, string handle, string duplicateHandle, string action,
        string reason)
    {
        return $"---\ntype: {objectType}\naction: {action}\nhandle: {handle}\nduplicate_handle: {duplicateHandle}\n---\n{reason}";
    }

    public static string[] MediaCreateNextSteps(string handle) => new[]
    {
        $"Show it on a person, family, event, place, source or citation: add it to that record's mediaHandles, e.g. update_person(handle: \"<person>\", mediaHandles: [\"{handle}\"], linkMode: \"add\"). Next time pass attachTo to upload_media instead.",
    };
}
