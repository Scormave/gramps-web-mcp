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
    /// and appends <c>[event: handle]</c> when the entry has a handle; <see cref="GrampsTimelineEntry.OtherParticipants"/>
    /// follow on a line of their own. A <paramref name="heading"/>,
    /// such as the place of a place timeline, comes before the event count.
    /// </summary>
    public static string FormatTimelineChronological(GrampsTimelineEntry[] entries, string? heading = null)
    {
        if (entries == null || entries.Length == 0)
            return "No events recorded";

        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(heading))
            sb.AppendLine(heading.Trim());
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
        var others = !string.IsNullOrWhiteSpace(entry.OtherParticipants)
            ? $"\n    Participants: {entry.OtherParticipants.Trim()}"
            : "";
        var desc = !string.IsNullOrWhiteSpace(entry.Description) ? $"\n    {entry.Description.Trim()}" : "";
        var handleSuffix = !string.IsNullOrWhiteSpace(entry.Handle)
            ? $"  [event: {entry.Handle.Trim()}]"
            : "";

        return $"  {dateStr}: {type}{who}{role}{age}{place}{handleSuffix}{others}{desc}";
    }

    /// <summary>
    /// The timeline person's own event under a role other than Primary, such as "Birth [Father]": the row
    /// alone does not say whose birth it is.
    /// </summary>
    internal static bool IsOwnEventInAnotherRole(GrampsTimelineEntry entry) =>
        string.IsNullOrWhiteSpace(entry.Person?.NameDisplay) && IsShownRole(entry.Role);

    /// <summary>
    /// The API omits the person for the timeline person's own events, so a name appears only for
    /// relatives, family members, and place participants.
    /// </summary>
    private static string FormatWho(GrampsTimelineEntry entry)
    {
        var person = entry.Person;
        if (!string.IsNullOrWhiteSpace(person?.NameDisplay))
        {
            var id = string.IsNullOrWhiteSpace(person.GrampsId) ? "" : $" ({person.GrampsId.Trim()})";
            return $": {person.NameDisplay.Trim()}{id}";
        }

        return string.IsNullOrWhiteSpace(entry.Participants) ? "" : $": {entry.Participants.Trim()}";
    }

    /// <summary>Primary is the usual role and only adds noise.</summary>
    private static bool IsShownRole(string? role) =>
        !string.IsNullOrWhiteSpace(role) && !role.Trim().Equals("Primary", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Age of the named person; for the timeline person's own events, their age. A zero age
    /// ("0 days" on birth rows) is left out.
    /// </summary>
    private static string FormatAge(GrampsTimelineEntry entry)
    {
        var age = !string.IsNullOrWhiteSpace(entry.Person?.NameDisplay) ? entry.Person.Age : entry.Age;
        return string.IsNullOrWhiteSpace(age) || IsZeroAge(age) ? "" : $", age {age.Trim()}";
    }

    /// <summary>
    /// Gramps writes the age in the server's language, so a zero age is told by its digits
    /// ("0 days", "0 дней") rather than by its words.
    /// </summary>
    private static bool IsZeroAge(string age)
    {
        var digits = age.Where(char.IsDigit).ToList();
        return digits.Count > 0 && digits.All(c => char.GetNumericValue(c) == 0);
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
