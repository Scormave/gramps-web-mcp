using System.Text.Json.Serialization;
using GrampsWeb.Mcp.Serialization;

namespace GrampsWeb.Mcp.Models;

/// <summary>
/// Gramps Web API date (Gregorian segments + modifiers). Wire format uses <c>dateval</c>; this type exposes structured fields.
/// </summary>
[JsonConverter(typeof(GrampsDateJsonConverter))]
public class GrampsDate
{
    // ── Scalar metadata (JSON: calendar, modifier, quality, text, newyear, sortval) ──

    public int Calendar { get; set; } // 0=Gregorian, 1=Julian, ...

    /// <summary>0=None, 1=Before, 2=After, 3=About, 4=Range, 5=Span, 6=TextOnly</summary>
    public int Modifier { get; set; }

    public int Quality { get; set; }

    public string? Text { get; set; }

    public int NewYear { get; set; }

    /// <summary>Server sort key (Gramps serial day number for Gregorian). Included on <see cref="Requests.DateRequest"/> writes when computable.</summary>
    public int? SortVal { get; set; }

    // ── First date segment (JSON dateval[0..3]: day, month, year, slash) ──

    public int Day { get; set; }

    public int Month { get; set; }

    public int Year { get; set; }

    /// <summary>Dual-year / "slash" date flag (Gramps dateval[3]).</summary>
    public bool Slash { get; set; }

    // ── Second segment for range (4) / span (5): dateval[4..7] ──

    public int EndDay { get; set; }

    public int EndMonth { get; set; }

    public int EndYear { get; set; }

    public bool EndSlash { get; set; }

    public static GrampsDate ExactDate(int day, int month, int year) => new()
    {
        Calendar = 0,
        Modifier = 0,
        Quality = 0,
        Day = day,
        Month = month,
        Year = year,
        Slash = false
    };

    public static GrampsDate YearOnly(int year) => new()
    {
        Calendar = 0,
        Modifier = 0,
        Quality = 0,
        Year = year
    };

    public static GrampsDate MonthYear(int month, int year) => new()
    {
        Calendar = 0,
        Modifier = 0,
        Quality = 0,
        Month = month,
        Year = year
    };

    public static GrampsDate About(int year) => new()
    {
        Calendar = 0,
        Modifier = 3,
        Quality = 0,
        Year = year
    };

    public static GrampsDate Before(int year) => new()
    {
        Calendar = 0,
        Modifier = 1,
        Quality = 0,
        Year = year
    };

    public static GrampsDate After(int year) => new()
    {
        Calendar = 0,
        Modifier = 2,
        Quality = 0,
        Year = year
    };

    public static GrampsDate Range(int year1, int year2) => new()
    {
        Calendar = 0,
        Modifier = 4,
        Quality = 0,
        Year = year1,
        EndYear = year2
    };

    public static GrampsDate Span(int year1, int year2) => new()
    {
        Calendar = 0,
        Modifier = 5,
        Quality = 0,
        Year = year1,
        EndYear = year2
    };

    public static GrampsDate TextOnly(string text) => new()
    {
        Calendar = 0,
        Modifier = 6,
        Quality = 0,
        Day = 0,
        Month = 0,
        Year = 0,
        Slash = false,
        Text = text
    };

    public static GrampsDate BCE(int year) => new()
    {
        Calendar = 0,
        Modifier = 0,
        Quality = 0,
        Year = -year
    };

    public string ToDisplayString()
    {
        if (Modifier == 6)
            return Text ?? "";

        string modifierPrefix = Modifier switch
        {
            1 => "Before ",
            2 => "After ",
            3 => "About ",
            4 => "From ",
            5 => "Between ",
            _ => ""
        };

        string dateStr;
        if (Modifier is 4 or 5)
            dateStr = $"{Year} to {EndYear}";
        else if (Day > 0 && Month > 0)
            dateStr = $"{Day}/{Month}/{Year}";
        else if (Month > 0)
            dateStr = $"{Month}/{Year}";
        else
            dateStr = $"{Year}";

        return modifierPrefix + dateStr;
    }
}
