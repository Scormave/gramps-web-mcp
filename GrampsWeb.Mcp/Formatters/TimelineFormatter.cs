using System.Text;
using System.Text.RegularExpressions;
using GrampsWeb.Mcp.Models;

namespace GrampsWeb.Mcp.Formatters;

/// <summary>
/// Formats timeline API responses for people, families, and places.
/// </summary>
public static class TimelineFormatter
{
    // Days and months take one or two digits, so a three- or four-digit number is the year (960, 0960, 1850).
    private static readonly Regex YearToken = new(@"\b[0-9]{3,4}\b", RegexOptions.Compiled);

    /// <summary>
    /// Renders timeline rows in chronological order (by decade when there are more than 20 rows).
    /// A row names the person the event belongs to unless it is the timeline person's own event,
    /// and appends <c>[event: handle]</c> when the entry has a handle.
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
        var who = FormatWho(entry);
        var role = IsShownRole(entry.Role) ? $" [{entry.Role!.Trim()}]" : "";
        var age = FormatAge(entry);
        var place = FormatPlaceSuffix(entry);
        var desc = !string.IsNullOrWhiteSpace(entry.Description) ? $"\n    {entry.Description.Trim()}" : "";
        var handleSuffix = !string.IsNullOrWhiteSpace(entry.Handle)
            ? $"  [event: {entry.Handle.Trim()}]"
            : "";

        return $"  {dateStr}: {type}{who}{role}{age}{place}{handleSuffix}{desc}";
    }

    /// <summary>
    /// The API omits the person for the timeline person's own events, so a name appears only for
    /// relatives and family members.
    /// </summary>
    private static string FormatWho(GrampsTimelineEntry entry)
    {
        var person = entry.Person;
        if (!string.IsNullOrWhiteSpace(person?.NameDisplay))
        {
            var id = string.IsNullOrWhiteSpace(person.GrampsId) ? "" : $" ({person.GrampsId.Trim()})";
            return $": {person.NameDisplay.Trim()}{id}";
        }

        return "";
    }

    /// <summary>Primary is the usual role and only adds noise.</summary>
    private static bool IsShownRole(string? role) =>
        !string.IsNullOrWhiteSpace(role) && !role.Trim().Equals("Primary", StringComparison.OrdinalIgnoreCase);

    /// <summary>Age of the named person; for the timeline person's own events, their age.</summary>
    private static string FormatAge(GrampsTimelineEntry entry)
    {
        var age = !string.IsNullOrWhiteSpace(entry.Person?.NameDisplay) ? entry.Person.Age : entry.Age;
        return string.IsNullOrWhiteSpace(age) ? "" : $", age {age.Trim()}";
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
