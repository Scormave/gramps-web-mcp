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
                   "Unlink them first with the owners' update tools, or pass force=true once the user confirmed " +
                   "deleting it anyway. " + ForceEffect(apiPath, LinksTo(apiPath, response.Value));
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
    /// "Delete Person I0001: Ivanov, Pyotr, b. 1880?" with what deleting it does to the records that link to it,
    /// in plain text: clients show the question as it is.
    /// </summary>
    private static async Task<string> FormatQuestionAsync(GrampsApiClient client, string objectType, string apiPath,
        string handle, JsonElement response, bool force)
    {
        var (removed, kept) = force ? LinksTo(apiPath, response) : ([], []);
        var labels = await LoadLabelsAsync(client, apiPath, handle,
            removed.Take(MaxBacklinksNamed).Concat(kept.Take(MaxBacklinksNamed)).ToList());

        var sb = new StringBuilder($"Delete {objectType}");
        if (response.TryGetProperty("gramps_id", out var grampsId) && grampsId.ValueKind == JsonValueKind.String
            && grampsId.GetString()?.Trim() is { Length: > 0 } id)
            sb.Append(' ').Append(id);
        if (labels.TryGetValue(handle, out var label))
            sb.Append(": ").Append(label);
        sb.AppendLine("?");

        if (removed.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine(apiPath == "sources"
                ? removed.Count == 1
                    ? "Gramps Web also deletes its citation and removes the links to it:"
                    : $"Gramps Web also deletes its {removed.Count} citations and removes the links to them:"
                : removed.Count == 1
                    ? "1 record links to it; Gramps Web removes that link:"
                    : $"{removed.Count} records link to it; Gramps Web removes those links:");
            AppendRecords(sb, removed, labels);
            if (apiPath == "people" && removed.Any(link => link.Collection == "families"))
                sb.AppendLine("A family left without father, mother and children is deleted with it.");
        }

        if (kept.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine(kept.Count == 1
                ? "1 place inside it keeps a link to the missing place:"
                : $"{kept.Count} places inside it keep links to the missing place:");
            AppendRecords(sb, kept, labels);
        }

        sb.AppendLine();
        sb.Append(removed.Count + kept.Count > 0
            ? "The change history keeps all of this: undo_change can restore it later."
            : "The change history keeps it: undo_change can restore it later.");
        return sb.ToString();
    }

    private static void AppendRecords(StringBuilder sb, IReadOnlyList<(string Collection, string Handle)> links,
        IReadOnlyDictionary<string, string> labels)
    {
        foreach (var (collection, h) in links.Take(MaxBacklinksNamed))
        {
            sb.Append("  • ").Append(ClassOf(collection));
            sb.AppendLine(labels.TryGetValue(h, out var linked) ? $": {linked}" : $" [handle: {h}]");
        }
        if (links.Count > MaxBacklinksNamed)
            sb.AppendLine($"  • … and {links.Count - MaxBacklinksNamed} more");
    }

    /// <summary>
    /// The records that link to a record of <paramref name="apiPath"/>, split by what a forced delete does to them:
    /// Gramps Web removes the links of most (and deletes a source's citations), but the places inside a deleted
    /// place keep their link to it.
    /// </summary>
    private static (List<(string Collection, string Handle)> Removed, List<(string Collection, string Handle)> Kept)
        LinksTo(string apiPath, JsonElement response)
    {
        var links = BacklinkCollector.ParseGroupsFromResponseRoot(response)
            .SelectMany(group => group.Handles.Select(h => (Collection: group.Key, Handle: h)))
            .ToList();
        bool Keeps((string Collection, string Handle) link) => apiPath == "places" && link.Collection == "places";
        return (links.Where(link => !Keeps(link)).ToList(), links.Where(Keeps).ToList());
    }

    /// <summary>What force=true does to the records that link to it, for the refusal.</summary>
    private static string ForceEffect(string apiPath,
        (List<(string Collection, string Handle)> Removed, List<(string Collection, string Handle)> Kept) links)
    {
        var sb = new StringBuilder();
        if (apiPath == "sources")
            sb.Append("Gramps Web then deletes its citations and removes the links to them.");
        else if (links.Removed.Count > 0)
            sb.Append("Gramps Web then removes their links to it.");
        if (apiPath == "people" && links.Removed.Any(link => link.Collection == "families"))
            sb.Append(" A family left without father, mother and children is deleted with it.");
        if (links.Kept.Count > 0)
            sb.Append(sb.Length > 0 ? " " : "").Append(
                "The places inside it keep a link to the missing place, so change their enclosedBy with update_place first.");
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
