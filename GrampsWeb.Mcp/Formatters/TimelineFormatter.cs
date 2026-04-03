using System.Text;
using GrampsWeb.Mcp.Models;

namespace GrampsWeb.Mcp.Formatters;

/// <summary>
/// Formats timeline API responses for people, families, and places.
/// </summary>
public static class TimelineFormatter
{
    public static string FormatTimeline(string header, GrampsTimelineEntry[] entries)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{header} ({entries.Length} events)");
        sb.AppendLine(new string('=', 60));
        sb.AppendLine();

        foreach (var entry in entries)
        {
            var dateStr  = entry.Date != null ? GrampsValueFormatter.FormatDate(entry.Date) : "—";
            var type     = entry.Type ?? "Event";
            var role     = !string.IsNullOrEmpty(entry.Role) ? $" [{entry.Role}]" : "";
            var place    = !string.IsNullOrEmpty(entry.Place) ? $" — {entry.Place}" : "";
            var relative = !string.IsNullOrEmpty(entry.Name) ? $" ({entry.Name})" : "";
            var rating   = entry.Rating.HasValue ? $" ★{entry.Rating:F1}" : "";
            var desc     = !string.IsNullOrEmpty(entry.Description) ? $"\n    {entry.Description}" : "";

            sb.AppendLine($"{dateStr}: {type}{role}{place}{relative}{rating}{desc}");
        }

        return sb.ToString();
    }

    public static string FormatTimelineChronological(GrampsTimelineEntry[] entries)
    {
        if (entries == null || entries.Length == 0)
            return "No events recorded";

        var sb = new StringBuilder();
        sb.AppendLine($"Timeline ({entries.Length} events):");
        sb.AppendLine(new string('=', 60));

        var sorted = entries
            .OrderBy(e => e.Date?.SortVal ?? int.MaxValue)
            .ToList();

        if (sorted.Count > 20)
        {
            var decades = sorted.GroupBy(e =>
            {
                if (e.Date != null && e.Date.Year > 0)
                    return (e.Date.Year / 10) * 10;
                return 0;
            });

            foreach (var decade in decades)
            {
                if (decade.Key > 0)
                    sb.AppendLine($"\n{decade.Key}s:");
                else
                    sb.AppendLine("\nUndated:");

                foreach (var entry in decade)
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
        var dateStr = entry.Date != null ? GrampsValueFormatter.FormatDate(entry.Date) : "—";
        var type = entry.Type ?? "Event";
        var role = !string.IsNullOrEmpty(entry.Role) ? $" [{entry.Role}]" : "";
        var place = !string.IsNullOrEmpty(entry.Place) ? $" — {entry.Place}" : "";
        var relative = !string.IsNullOrEmpty(entry.Name) ? $" ({entry.Name})" : "";
        var rating = entry.Rating.HasValue ? $" ★{entry.Rating:F1}" : "";
        var desc = !string.IsNullOrEmpty(entry.Description) ? $"\n    {entry.Description}" : "";

        return $"  {dateStr}: {type}{role}{place}{relative}{rating}{desc}";
    }
}
