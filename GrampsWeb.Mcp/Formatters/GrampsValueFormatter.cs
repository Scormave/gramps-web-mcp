using System.Text;
using System.Text.Json;
using GrampsWeb.Mcp.Models;

namespace GrampsWeb.Mcp.Formatters;

/// <summary>
/// Formats Gramps value types (date, name, simple place) used across entity formatters.
/// </summary>
public static class GrampsValueFormatter
{
    private static readonly string[] MonthNames =
    {
        "", "Jan", "Feb", "Mar", "Apr", "May", "Jun",
        "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"
    };

    public static string FormatDate(GrampsDate date)
    {
        if (date == null)
            return "Unknown date";

        if (date.Modifier == 6)
            return date.Text ?? "Unknown date";

        if (date.Day == 0 && date.Month == 0 && date.Year == 0 && !date.Slash
            && date.Modifier is not (4 or 5))
            return date.Text ?? "Unknown date";

        int day = date.Day, month = date.Month, year = date.Year;
        bool slash = date.Slash;

        string formattedDate = FormatDateComponents(day, month, year, slash);

        return date.Modifier switch
        {
            1 => $"before {formattedDate}",
            2 => $"after {formattedDate}",
            3 => $"about {formattedDate}",
            4 => FormatDateRange(date),
            5 => FormatDateSpan(date),
            _ => formattedDate
        };
    }

    private static string FormatDateRange(GrampsDate date)
    {
        var date1 = FormatDateComponents(date.Day, date.Month, date.Year, date.Slash);
        var date2 = FormatDateComponents(date.EndDay, date.EndMonth, date.EndYear, date.EndSlash);
        if (string.IsNullOrWhiteSpace(date1) && string.IsNullOrWhiteSpace(date2))
            return "unknown date range";
        return $"between {date1} and {date2}";
    }

    private static string FormatDateSpan(GrampsDate date)
    {
        var date1 = FormatDateComponents(date.Day, date.Month, date.Year, date.Slash);
        var date2 = FormatDateComponents(date.EndDay, date.EndMonth, date.EndYear, date.EndSlash);
        if (string.IsNullOrWhiteSpace(date1) && string.IsNullOrWhiteSpace(date2))
            return "unknown date span";
        return $"from {date1} to {date2}";
    }

    private static string FormatDateComponents(int day, int month, int year, bool slash)
    {
        var sb = new StringBuilder();

        if (year < 0)
        {
            if (day > 0 || month > 0)
            {
                if (day > 0)
                    sb.Append($"{day} ");
                if (month > 0 && month <= 12)
                    sb.Append($"{MonthNames[month]} ");
            }
            sb.Append($"{Math.Abs(year)} B.C.E.");
            return sb.ToString();
        }

        if (day > 0)
            sb.Append($"{day} ");

        if (month > 0 && month <= 12)
            sb.Append($"{MonthNames[month]} ");

        if (year > 0)
        {
            sb.Append(year);
            if (slash)
                sb.Append("/").Append(year + 1);
        }

        return sb.ToString().Trim();
    }

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
                var sn = new StringBuilder();

                if (!string.IsNullOrEmpty(surname.Prefix))
                    sn.Append(surname.Prefix).Append(" ");

                if (!string.IsNullOrEmpty(surname.Surname))
                    sn.Append(surname.Surname);

                if (!string.IsNullOrEmpty(surname.Connector))
                    sn.Append(" ").Append(surname.Connector);

                surnameParts.Add(sn.ToString().Trim());
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

    /// <summary>
    /// Returns a multi-line indented breakdown of a name showing each component with its label.
    /// Each line starts with <paramref name="indent"/>.
    /// </summary>
    public static string FormatNameDetailed(GrampsName? name, string indent = "    ")
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
                    meta.Add(s.OriginType);
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
