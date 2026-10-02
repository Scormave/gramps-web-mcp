using System.Text;
using System.Text.Json;
using GrampsWeb.Mcp.Models;

namespace GrampsWeb.Mcp.Formatters;

/// <summary>
/// Formats Gramps value types (date, name, simple place) used across entity formatters.
/// </summary>
public static class GrampsValueFormatter
{
    /// <summary>Gramps date qualities by code; regular dates have none.</summary>
    private static readonly string[] Qualities = ["", "estimated ", "calculated "];

    /// <summary>Gramps calendars by code; Gramps names every calendar but the Gregorian.</summary>
    private static readonly string[] Calendars = ["", "Julian", "Hebrew", "French Republican", "Persian", "Islamic", "Swedish"];

    /// <summary>Gramps new-year days by code; Gramps names every start but 1 January.</summary>
    private static readonly string[] NewYears = ["", "Mar1", "Mar25", "Sep1"];

    /// <summary>
    /// Formats a date as Gramps Web shows it in its default ISO format, so cards agree with the dates the
    /// server formats in timelines and search results: <c>1856-08-01</c>, <c>estimated about 1930-08</c>,
    /// <c>between 1850 and 1860</c>, <c>1856-07-20 (Julian)</c>.
    /// </summary>
    public static string FormatDate(GrampsDate date)
    {
        if (date == null)
            return "Unknown date";

        if (date.Modifier == 6)
            return string.IsNullOrWhiteSpace(date.Text) ? "Unknown date" : date.Text.Trim();

        if (date.Day == 0 && date.Month == 0 && date.Year == 0 && !date.Slash
            && date.Modifier is not (4 or 5 or 7 or 8))
            return string.IsNullOrWhiteSpace(date.Text) ? "Unknown date" : date.Text.Trim();

        string formattedDate = FormatDateComponents(date.Day, date.Month, date.Year, date.Slash);

        var text = date.Modifier switch
        {
            1 => $"before {formattedDate}",
            2 => $"after {formattedDate}",
            3 => $"about {formattedDate}",
            4 => FormatDateRange(date),
            5 => FormatDateSpan(date),
            7 => $"from {formattedDate}",
            8 => $"to {formattedDate}",
            _ => formattedDate
        };

        return $"{NameOf(Qualities, date.Quality)}{text}{FormatCalendarExtras(date)}";
    }

    private static string FormatDateRange(GrampsDate date)
    {
        var date1 = FormatDateComponents(date.Day, date.Month, date.Year, date.Slash);
        var date2 = FormatDateComponents(date.EndDay, date.EndMonth, date.EndYear, date.EndSlash);
        if (string.IsNullOrWhiteSpace(date1) && string.IsNullOrWhiteSpace(date2))
            return "unknown date range";
        if (string.IsNullOrWhiteSpace(date1))
            return $"before {date2}";
        if (string.IsNullOrWhiteSpace(date2))
            return $"after {date1}";
        return $"between {date1} and {date2}";
    }

    private static string FormatDateSpan(GrampsDate date)
    {
        var date1 = FormatDateComponents(date.Day, date.Month, date.Year, date.Slash);
        var date2 = FormatDateComponents(date.EndDay, date.EndMonth, date.EndYear, date.EndSlash);
        if (string.IsNullOrWhiteSpace(date1) && string.IsNullOrWhiteSpace(date2))
            return "unknown date span";
        if (string.IsNullOrWhiteSpace(date1))
            return $"until {date2}";
        if (string.IsNullOrWhiteSpace(date2))
            return $"from {date1}";
        return $"from {date1} to {date2}";
    }

    /// <summary>One side of a date as Gramps writes it in ISO: <c>1856</c>, <c>1856-08</c>, <c>1856-08-01</c>.</summary>
    private static string FormatDateComponents(int day, int month, int year, bool slash)
    {
        if (day == 0 && month == 0 && year == 0 && !slash)
            return "";

        var value = FormatYear(year, slash);
        if (day != 0)
            value += $"-{month:00}-{day:00}";
        else if (month != 0)
            value += $"-{month:00}";

        return year < 0 ? $"{value} B.C.E." : value;
    }

    /// <summary>
    /// Gramps keeps the later year of a dual-dated year and shortens it after the slash: 1736 reads <c>1735/6</c>.
    /// </summary>
    private static string FormatYear(int year, bool slash)
    {
        var value = Math.Abs(year);
        if (!slash)
            return $"{value}";

        var previous = value - 1;
        var shortened = previous % 100 == 99 ? value % 1000 : previous % 10 == 9 ? value % 100 : value % 10;
        return $"{previous}/{shortened}";
    }

    /// <summary>A non-Gregorian calendar and a new year not on 1 January: <c> (Julian, Mar25)</c>.</summary>
    private static string FormatCalendarExtras(GrampsDate date)
    {
        var extras = new[] { NameOf(Calendars, date.Calendar), NameOf(NewYears, date.NewYear) }
            .Where(name => name.Length > 0)
            .ToList();
        return extras.Count == 0 ? "" : $" ({string.Join(", ", extras)})";
    }

    private static string NameOf(string[] names, int code) => code > 0 && code < names.Length ? names[code] : "";

    public static string FormatName(GrampsName? name)
    {
        if (name == null)
            return "Unknown";

        var parts = new List<string>();

        if (!string.IsNullOrEmpty(name.Title))
            parts.Add(name.Title);

        if (!string.IsNullOrEmpty(name.FirstName))
            parts.Add(name.FirstName);

        if (name.SurnameList != null && name.SurnameList.Length > 0)
        {
            var surnameParts = new List<string>();
            foreach (var surname in name.SurnameList)
            {
                var formatted = FormatSurnameSegment(surname);
                if (string.IsNullOrEmpty(formatted))
                    continue;

                surnameParts.Add(formatted);
            }

            if (surnameParts.Count > 0)
                parts.Add(string.Join(" ", surnameParts));
        }

        if (!string.IsNullOrEmpty(name.Suffix))
            parts.Add(name.Suffix);

        var notes = new List<string>();
        if (!string.IsNullOrEmpty(name.Call))
            notes.Add(name.Call);
        if (!string.IsNullOrEmpty(name.Nick))
            notes.Add($"'{name.Nick}'");
        if (!string.IsNullOrEmpty(name.FamNick))
            notes.Add(name.FamNick);

        if (notes.Count > 0)
            parts.Add($"({string.Join(", ", notes)})");

        return string.Join(" ", parts).Trim();
    }

    private static string FormatSurnameSegment(GrampsSurname surname)
    {
        var sn = new StringBuilder();

        if (!string.IsNullOrEmpty(surname.Prefix))
            sn.Append(surname.Prefix).Append(' ');

        if (!string.IsNullOrEmpty(surname.Surname))
            sn.Append(surname.Surname);

        if (!string.IsNullOrEmpty(surname.Connector))
            sn.Append(' ').Append(surname.Connector);

        return sn.ToString().Trim();
    }

    /// <summary>
    /// Returns a multi-line indented breakdown of a name showing each component with its label.
    /// Each line starts with <paramref name="indent"/>.
    /// </summary>
    public static string FormatNameDetailed(
        GrampsName? name,
        string indent = "    ",
        IReadOnlyList<string>? originTypeLabels = null)
    {
        if (name == null)
            return $"{indent}(no name data)";

        var lines = new List<string>();

        if (!string.IsNullOrEmpty(name.Title))
            lines.Add($"{indent}title:   {name.Title}");

        if (!string.IsNullOrEmpty(name.FirstName))
            lines.Add($"{indent}first:   {name.FirstName}");

        if (!string.IsNullOrEmpty(name.Call))
            lines.Add($"{indent}call:    {name.Call}");

        if (!string.IsNullOrEmpty(name.Nick))
            lines.Add($"{indent}nick:    {name.Nick}");

        if (!string.IsNullOrEmpty(name.FamNick))
            lines.Add($"{indent}famnick: {name.FamNick}");

        if (name.SurnameList is { Length: > 0 })
        {
            foreach (var s in name.SurnameList)
            {
                var sn = new StringBuilder();
                if (!string.IsNullOrEmpty(s.Prefix))
                    sn.Append(s.Prefix).Append(' ');
                if (!string.IsNullOrEmpty(s.Surname))
                    sn.Append(s.Surname);
                if (!string.IsNullOrEmpty(s.Connector))
                    sn.Append(' ').Append(s.Connector);

                var snStr = sn.ToString().Trim();
                if (string.IsNullOrEmpty(snStr))
                    continue;

                var meta = new List<string>();
                if (s.Primary)
                    meta.Add("primary");
                if (!string.IsNullOrEmpty(s.Prefix))
                    meta.Add($"prefix: {s.Prefix}");
                if (!string.IsNullOrEmpty(s.OriginType))
                {
                    meta.Add(GrampsDefaultTypeLabels.ResolveStored(s.OriginType, originTypeLabels));
                }
                if (!string.IsNullOrEmpty(s.Connector))
                    meta.Add($"connector: {s.Connector}");

                var metaStr = meta.Count > 0 ? $" [{string.Join(", ", meta)}]" : "";
                lines.Add($"{indent}surname: {snStr}{metaStr}");
            }
        }

        if (!string.IsNullOrEmpty(name.Suffix))
            lines.Add($"{indent}suffix:  {name.Suffix}");

        return lines.Count > 0
            ? string.Join(Environment.NewLine, lines)
            : $"{indent}(empty name)";
    }

    public static string FormatPlace(GrampsPlace place)
    {
        if (place == null)
            return "Unknown place";

        if (string.IsNullOrEmpty(place.Name))
            return "Unknown place";

        return place.Name;
    }

    /// <summary>Used by timeline decade grouping and legacy dateval parsing.</summary>
    internal static int ToInt(object? val) => val switch
    {
        int i    => i,
        long l   => (int)l,
        decimal d => (int)d,
        double dbl => (int)dbl,
        JsonElement je when je.ValueKind == JsonValueKind.Number => je.GetInt32(),
        _ => 0
    };
}
