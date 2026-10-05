using System.Text;
using System.Text.Json;
using GrampsWeb.Mcp.Models;

namespace GrampsWeb.Mcp.Formatters;

/// <summary>
/// Formats system-level API responses (metadata, transactions, bookmarks).
/// </summary>
public static class SystemFormatter
{
    private static readonly System.Globalization.TextInfo TextInfo =
        System.Globalization.CultureInfo.CurrentCulture.TextInfo;

    /// <param name="defaultPersonFullName">
    /// When set and <c>default_person</c> in metadata is a string handle, output includes this display name
    /// plus the handle on a separate line instead of only the handle.
    /// </param>
    public static string FormatMetadata(JsonElement metadata, string? defaultPersonFullName = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine("DATABASE METADATA");
        sb.AppendLine(new string('=', 60));
        sb.AppendLine();

        try
        {
            foreach (var property in metadata.EnumerateObject())
            {
                if (property.Name == "default_person"
                    && defaultPersonFullName != null
                    && property.Value.ValueKind == JsonValueKind.String)
                {
                    var handle = property.Value.GetString() ?? "";
                    sb.AppendLine(ToDisplayLabel(property.Name));
                    var indent = new string(' ', 2);
                    sb.AppendLine($"{indent}{"Name",-28} {defaultPersonFullName}");
                    sb.AppendLine($"{indent}{"Handle",-28} {handle}");
                    continue;
                }

                var label = ToDisplayLabel(property.Name);
                AppendMetadataProperty(sb, label, property.Value);
            }
        }
        catch { }

        return sb.ToString();
    }

    private static string ToDisplayLabel(string key) => TextInfo.ToTitleCase(key.Replace('_', ' '));

    private static void AppendMetadataProperty(StringBuilder sb, string label, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                sb.AppendLine(label);
                foreach (var child in value.EnumerateObject())
                {
                    AppendIndentedValue(sb, 1, ToDisplayLabel(child.Name), child.Value);
                }
                break;
            case JsonValueKind.Array:
                sb.AppendLine(label);
                AppendArray(sb, value, 1);
                break;
            default:
                sb.AppendLine($"{label,-30} {GetScalarValue(value)}");
                break;
        }
    }

    private static void AppendIndentedValue(StringBuilder sb, int indentLevel, string label, JsonElement value)
    {
        var indent = new string(' ', indentLevel * 2);
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                sb.AppendLine($"{indent}{label}:");
                foreach (var child in value.EnumerateObject())
                {
                    AppendIndentedValue(sb, indentLevel + 1, ToDisplayLabel(child.Name), child.Value);
                }
                break;
            case JsonValueKind.Array:
                sb.AppendLine($"{indent}{label}:");
                AppendArray(sb, value, indentLevel + 1);
                break;
            default:
                sb.AppendLine($"{indent}{label,-28} {GetScalarValue(value)}");
                break;
        }
    }

    private static void AppendArray(StringBuilder sb, JsonElement value, int indentLevel)
    {
        var indent = new string(' ', indentLevel * 2);
        int idx = 1;
        foreach (var item in value.EnumerateArray())
        {
            switch (item.ValueKind)
            {
                case JsonValueKind.Object:
                    sb.AppendLine($"{indent}-");
                    foreach (var child in item.EnumerateObject())
                    {
                        AppendIndentedValue(sb, indentLevel + 1, ToDisplayLabel(child.Name), child.Value);
                    }
                    break;
                case JsonValueKind.Array:
                    sb.AppendLine($"{indent}{idx}.");
                    AppendArray(sb, item, indentLevel + 1);
                    idx++;
                    break;
                default:
                    sb.AppendLine($"{indent}- {GetScalarValue(item)}");
                    break;
            }
        }
    }

    private static string GetScalarValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? string.Empty,
        JsonValueKind.Number => value.ToString(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Null => "null",
        _ => value.ToString()
    };

    internal const int MaxChangesShownPerTransaction = 10;

    /// <summary>
    /// "1. 2026-09-30 14:22:05 UTC — Edit Person — by Jane Doe [transaction: 42]" followed by one line per
    /// changed object; long transactions such as imports are cut after <see cref="MaxChangesShownPerTransaction"/>.
    /// </summary>
    public static string FormatRecentChanges(IReadOnlyList<GrampsTransaction> transactions, int totalCount = -1)
    {
        var sb = new StringBuilder();
        sb.AppendLine(totalCount >= 0
            ? $"RECENT CHANGES ({transactions.Count} of {totalCount}, newest first)"
            : "RECENT CHANGES (newest first)");
        sb.AppendLine(new string('=', 60));
        sb.AppendLine();

        if (transactions.Count == 0)
        {
            sb.AppendLine("No recent changes found.");
            return sb.ToString();
        }

        for (var i = 0; i < transactions.Count; i++)
        {
            var transaction = transactions[i];
            var parts = new List<string>();
            if (FormatUnixTime(transaction.Timestamp ?? transaction.Connection?.Timestamp) is { } time)
                parts.Add(time);
            parts.Add(string.IsNullOrWhiteSpace(transaction.Description) ? "(no description)" : transaction.Description.Trim());
            if (FormatUser(transaction.Connection?.User) is { } user)
                parts.Add($"by {user}");
            var undo = transaction.Undo ? " (undo)" : "";
            sb.AppendLine($"{i + 1}. {string.Join(" — ", parts)}{undo} [transaction: {transaction.Id}]");

            var changes = transaction.Changes ?? [];
            foreach (var change in changes.Take(MaxChangesShownPerTransaction))
            {
                var kind = change.TransType switch
                {
                    0 => "Added",
                    1 => "Updated",
                    2 => "Deleted",
                    _ => "Changed"
                };
                sb.AppendLine($"   {kind} {FormatChangedObject(change)}");
            }

            if (changes.Length > MaxChangesShownPerTransaction)
                sb.AppendLine($"   … (+{changes.Length - MaxChangesShownPerTransaction} more changes)");
        }

        return sb.ToString();
    }

    /// <summary>
    /// "Person [handle: …]", or "Reference from [handle: …] to [handle: …]" for a link: Gramps logs each link
    /// an edit adds or removes as a change of REFERENCE_KEY, which Gramps Web sends as class "7".
    /// </summary>
    private static string FormatChangedObject(GrampsTransactionChange change)
    {
        var handle = string.IsNullOrWhiteSpace(change.ObjHandle) ? null : $"[handle: {change.ObjHandle.Trim()}]";
        var objClass = string.IsNullOrWhiteSpace(change.ObjClass) ? "object" : change.ObjClass.Trim();
        if (objClass != "7")
            return handle == null ? objClass : $"{objClass} {handle}";

        var from = handle == null ? "" : $" from {handle}";
        var to = string.IsNullOrWhiteSpace(change.RefHandle) ? "" : $" to [handle: {change.RefHandle.Trim()}]";
        return $"Reference{from}{to}";
    }

    private static string? FormatUnixTime(double? seconds)
    {
        if (seconds is not { } value || double.IsNaN(value) || value <= 0)
            return null;
        return DateTimeOffset.FromUnixTimeMilliseconds((long)(value * 1000))
            .UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string? FormatUser(GrampsTransactionUser? user)
    {
        if (!string.IsNullOrWhiteSpace(user?.FullName))
            return user.FullName.Trim();
        return string.IsNullOrWhiteSpace(user?.Name) ? null : user.Name.Trim();
    }

    /// <summary>Bookmark collections in the order they are listed; others follow by name.</summary>
    private static readonly string[] BookmarkCollections =
        ["people", "families", "events", "places", "sources", "citations", "repositories", "media", "notes"];

    /// <summary>
    /// The tree's bookmarks from <c>GET /api/bookmarks/</c>, which answers <c>{"people": [handles], "families": [], …}</c>:
    /// one section per collection that has any, each bookmark named from <paramref name="labels"/>
    /// (<see cref="LinkedObjectLabels"/>) when it has the handle.
    /// </summary>
    public static string FormatBookmarks(
        IReadOnlyDictionary<string, string[]?> bookmarks,
        IReadOnlyDictionary<string, string>? labels = null)
    {
        var sections = bookmarks
            .Select(pair => (Collection: pair.Key, Handles: (pair.Value ?? [])
                .Where(h => !string.IsNullOrWhiteSpace(h))
                .Select(h => h.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToArray()))
            .Where(section => section.Handles.Length > 0)
            .OrderBy(section => Array.IndexOf(BookmarkCollections, section.Collection) is var i and >= 0 ? i : BookmarkCollections.Length)
            .ThenBy(section => section.Collection, StringComparer.Ordinal)
            .ToList();
        var count = sections.Sum(section => section.Handles.Length);

        var sb = new StringBuilder();
        sb.AppendLine(count == 0 ? "BOOKMARKS" : $"BOOKMARKS ({count})");
        sb.AppendLine(new string('=', 60));
        if (count == 0)
        {
            sb.AppendLine();
            sb.AppendLine("No bookmarks in this tree.");
        }

        foreach (var (collection, handles) in sections)
            HandleListFormatter.AppendHandleBulletSection(sb, TextInfo.ToTitleCase(collection), handles, labels);
        return sb.ToString();
    }
}
