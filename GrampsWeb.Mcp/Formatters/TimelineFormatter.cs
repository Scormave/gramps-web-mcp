using System.Text;
using System.Text.RegularExpressions;
using GrampsWeb.Mcp.Models;

namespace GrampsWeb.Mcp.Formatters;

/// <summary>
/// Formats timeline API responses for people, families, and places.
/// </summary>
public static class TimelineFormatter
{
    private static readonly Regex YearToken = new(@"\b(1[0-9]{3}|20[0-9]{2})\b", RegexOptions.Compiled);

    public static string FormatTimeline(string header, GrampsTimelineEntry[] entries)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{header} ({entries.Length} events)");
        sb.AppendLine(new string('=', 60));
        sb.AppendLine();

        foreach (var entry in entries)
        {
            var dateStr = FormatDateText(entry);
            var type = EventHeading(entry);
            var role = !string.IsNullOrEmpty(entry.Role) ? $" [{entry.Role}]" : "";
            var place = FormatPlaceSuffix(entry);
            var relative = !string.IsNullOrEmpty(entry.Name) ? $" ({entry.Name})" : "";
            var rating = entry.Rating.HasValue ? $" ★{entry.Rating:F1}" : "";
            var desc = !string.IsNullOrEmpty(entry.Description) ? $"\n    {entry.Description}" : "";
            var handleSuffix = !string.IsNullOrWhiteSpace(entry.Handle)
                ? $"  [event: {entry.Handle.Trim()}]"
                : "";

            sb.AppendLine($"{dateStr}: {type}{role}{place}{relative}{rating}{desc}{handleSuffix}");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Renders timeline rows in chronological order (by decade when there are more than 20 rows).
    /// Each row appends <c>[event: handle]</c> when the entry has a handle.
    /// </summary>
    public static string FormatTimelineChronological(GrampsTimelineEntry[] entries)
    {
        if (entries == null || entries.Length == 0)
            return "No events recorded";

        var sb = new StringBuilder();
        sb.AppendLine($"Timeline ({entries.Length} events):");
        sb.AppendLine(new string('=', 60));

        var sorted = entries
            .OrderBy(e => SortKeyFromDateDisplay(e.Date))
            .ToList();

        if (sorted.Count > 20)
        {
            var decades = sorted.GroupBy(e => DecadeFromDateDisplay(e.Date));

            foreach (var decade in decades.OrderBy(g => g.Key == 0 ? int.MaxValue : g.Key))
            {
                if (decade.Key > 0)
                    sb.AppendLine($"\n{decade.Key}s:");
                else
                    sb.AppendLine("\nUndated:");

                foreach (var entry in decade.OrderBy(e => SortKeyFromDateDisplay(e.Date)))
                    sb.AppendLine(FormatTimelineEntry(entry));
            }
        }
        else
        {
            foreach (var entry in sorted)
                sb.AppendLine(FormatTimelineEntry(entry));
        }

        return sb.ToString();
    }

    private static string FormatTimelineEntry(GrampsTimelineEntry entry)
    {
        var dateStr = FormatDateText(entry);
        var type = EventHeading(entry);
        var role = !string.IsNullOrEmpty(entry.Role) ? $" [{entry.Role}]" : "";
        var place = FormatPlaceSuffix(entry);
        var relative = !string.IsNullOrEmpty(entry.Name) ? $" ({entry.Name})" : "";
        var rating = entry.Rating.HasValue ? $" ★{entry.Rating:F1}" : "";
        var desc = !string.IsNullOrEmpty(entry.Description) ? $"\n    {entry.Description}" : "";
        var handleSuffix = !string.IsNullOrWhiteSpace(entry.Handle)
            ? $"  [event: {entry.Handle.Trim()}]"
            : "";

        return $"  {dateStr}: {type}{role}{place}{relative}{rating}{desc}{handleSuffix}";
    }

    private static string FormatDateText(GrampsTimelineEntry entry) =>
        string.IsNullOrWhiteSpace(entry.Date) ? "—" : entry.Date.Trim();

    private static string EventHeading(GrampsTimelineEntry entry) =>
        !string.IsNullOrWhiteSpace(entry.Label)
            ? entry.Label.Trim()
            : (!string.IsNullOrWhiteSpace(entry.Type) ? entry.Type.Trim() : "Event");

    private static string FormatPlaceSuffix(GrampsTimelineEntry entry)
    {
        var p = entry.Place;
        if (p == null)
            return "";
        var text = !string.IsNullOrWhiteSpace(p.DisplayName)
            ? p.DisplayName.Trim()
            : (!string.IsNullOrWhiteSpace(p.Name) ? p.Name.Trim() : null);
        return string.IsNullOrEmpty(text) ? "" : $" — {text}";
    }

    /// <summary>Coarse sort key from API display date (year * 10000); undated last.</summary>
    internal static int SortKeyFromDateDisplay(string? dateDisplay)
    {
        var y = ExtractYear(dateDisplay);
        return y.HasValue ? y.Value * 10000 : int.MaxValue;
    }

    internal static int DecadeFromDateDisplay(string? dateDisplay)
    {
        var y = ExtractYear(dateDisplay);
        if (y is > 0)
            return (y.Value / 10) * 10;
        return 0;
    }

    private static int? ExtractYear(string? dateDisplay)
    {
        if (string.IsNullOrWhiteSpace(dateDisplay))
            return null;
        var m = YearToken.Match(dateDisplay);
        if (!m.Success || !int.TryParse(m.Value, out var y))
            return null;
        return y;
    }
}
