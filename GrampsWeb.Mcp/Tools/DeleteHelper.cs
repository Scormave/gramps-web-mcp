using System.Text;
using System.Text.Json;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Formatters;

namespace GrampsWeb.Mcp.Tools;

internal static class DeleteHelper
{
    /// <summary>Records that keep a link to the deleted one, named in the confirmation question.</summary>
    internal const int MaxBacklinksNamed = 5;

    /// <summary>
    /// Shared delete-with-backlinks logic for all entity tools. When <paramref name="confirmation"/> asks,
    /// the user confirms in the client first, and the record is read again before it is deleted, so a
    /// record changed or relinked while the user was deciding is not deleted.
    /// </summary>
    public static async Task<string> DeleteWithBacklinksAsync(
        GrampsApiClient client,
        string objectType,
        string apiPath,
        string handle,
        bool force,
        string? originalIdentifier = null,
        IUserConfirmation? confirmation = null,
        CancellationToken cancellationToken = default)
    {
        originalIdentifier ??= handle;
        confirmation ??= UserConfirmation.None;
        var response = await ReadWithBacklinksAsync(client, apiPath, handle);
        if (response is null)
            return NotFoundHelper.NotFoundMessage(objectType, originalIdentifier);

        if (!force && FormatBacklinkCounts(response.Value) is { Length: > 0 } refs)
        {
            return $"Cannot delete {objectType} [{handle}]: other objects reference it.\n" +
                   $"Backlinks:\n{refs}" +
                   "Pass force=true to delete anyway (may leave dangling references).";
        }

        if (confirmation.Asks)
        {
            client.EnsureWritable();
            var question = await FormatQuestionAsync(client, objectType, apiPath, handle, response.Value, force);
            var answer = await confirmation.AskAsync(question, cancellationToken);
            if (answer != ConfirmationAnswer.Confirmed)
                return ResponseEnvelope.DeleteNotDone(objectType, handle, UserConfirmation.ActionName(answer),
                    UserConfirmation.Explain(answer));

            var current = await ReadWithBacklinksAsync(client, apiPath, handle);
            if (current is null)
                return NotFoundHelper.NotFoundMessage(objectType, originalIdentifier);
            if (Digest(current.Value) != Digest(response.Value))
            {
                return ResponseEnvelope.DeleteNotDone(objectType, handle, "refused",
                    "The record or the records linking to it changed while the user was deciding, so nothing was " +
                    "deleted. Read it again and ask the user before calling again.");
            }
        }

        await client.DeleteAsync($"/api/{apiPath}/{Uri.EscapeDataString(handle)}");
        return ResponseEnvelope.DeleteSuccess(objectType, handle);
    }

    private static async Task<JsonElement?> ReadWithBacklinksAsync(GrampsApiClient client, string apiPath, string handle)
    {
        var payload = await client.GetJsonOrNullIfNotFoundAsync(
            $"/api/{apiPath}/{Uri.EscapeDataString(handle)}?backlinks=true");
        return payload is null || payload.Value.ValueKind == JsonValueKind.Null ? null : payload;
    }

    /// <summary>"  people: 2 reference(s)" per backlink type; empty without backlinks.</summary>
    private static string FormatBacklinkCounts(JsonElement response)
    {
        var refs = new StringBuilder();
        if (response.TryGetProperty("backlinks", out var backlinks) && backlinks.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in backlinks.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.Array && prop.Value.GetArrayLength() > 0)
                    refs.AppendLine($"  {prop.Name}: {prop.Value.GetArrayLength()} reference(s)");
            }
        }

        return refs.ToString();
    }

    /// <summary>The last change time and the backlinks: what the user saw when confirming.</summary>
    private static string Digest(JsonElement response) =>
        (response.TryGetProperty("change", out var change) ? change.GetRawText() : "") + "|" +
        (response.TryGetProperty("backlinks", out var backlinks) ? backlinks.GetRawText() : "");

    /// <summary>
    /// "Delete Person I0001: Ivanov, Pyotr, b. 1880?" with the records that will keep a link to it,
    /// in plain text: clients show the question as it is.
    /// </summary>
    private static async Task<string> FormatQuestionAsync(GrampsApiClient client, string objectType, string apiPath,
        string handle, JsonElement response, bool force)
    {
        var backlinks = force ? BacklinkCollector.ParseGroupsFromResponseRoot(response) : [];
        var named = backlinks
            .SelectMany(group => group.Handles.Select(h => (Collection: group.Key, Handle: h)))
            .ToList();
        var labels = await LoadLabelsAsync(client, apiPath, handle, named.Take(MaxBacklinksNamed).ToList());

        var sb = new StringBuilder($"Delete {objectType}");
        if (response.TryGetProperty("gramps_id", out var grampsId) && grampsId.ValueKind == JsonValueKind.String
            && grampsId.GetString()?.Trim() is { Length: > 0 } id)
            sb.Append(' ').Append(id);
        if (labels.TryGetValue(handle, out var label))
            sb.Append(": ").Append(label);
        sb.AppendLine("?");

        if (named.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine(named.Count == 1
                ? "1 record still links to it and will keep a link to a missing record:"
                : $"{named.Count} records still link to it and will keep links to a missing record:");
            foreach (var (collection, h) in named.Take(MaxBacklinksNamed))
            {
                sb.Append("  • ").Append(ClassOf(collection));
                sb.AppendLine(labels.TryGetValue(h, out var linked) ? $": {linked}" : $" [handle: {h}]");
            }
            if (named.Count > MaxBacklinksNamed)
                sb.AppendLine($"  • … and {named.Count - MaxBacklinksNamed} more");
        }

        sb.AppendLine();
        sb.Append("The change history keeps it: undo_change can restore it later.");
        return sb.ToString();
    }

    /// <summary>Labels of the record and the named backlinks; without them the question still asks.</summary>
    private static async Task<IReadOnlyDictionary<string, string>> LoadLabelsAsync(GrampsApiClient client,
        string apiPath, string handle, IReadOnlyList<(string Collection, string Handle)> backlinks)
    {
        try
        {
            return await LinkedObjectLabels.LoadAsync(client,
                backlinks.Prepend((Collection: apiPath, Handle: handle))
                    .GroupBy(link => link.Collection)
                    .Select(group => (group.Key, (IEnumerable<string>?)group.Select(link => link.Handle).ToList())));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new Dictionary<string, string>();
        }
    }

    private static string ClassOf(string collection) => collection switch
    {
        "people" => "Person",
        "families" => "Family",
        "events" => "Event",
        "places" => "Place",
        "sources" => "Source",
        "citations" => "Citation",
        "notes" => "Note",
        "media" => "Media",
        "repositories" => "Repository",
        "tags" => "Tag",
        _ => "Record"
    };
}
