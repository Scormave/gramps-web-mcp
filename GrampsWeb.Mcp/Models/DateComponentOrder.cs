namespace GrampsWeb.Mcp.Models;

/// <summary>
/// How to interpret numeric dates with slashes or dots when they are not ISO-8601.
/// </summary>
public enum DateComponentOrder
{
    /// <summary>Prefer yyyy-MM-dd, yyyy-MM, or yyyy only. Slash/dot forms must not be ambiguous.</summary>
    Iso = 0,

    /// <summary>dd/MM/yyyy, dd-MM-yyyy, or dd.MM.yyyy (day first).</summary>
    DayMonthYear = 1,

    /// <summary>MM/dd/yyyy, MM-dd-yyyy, or MM.dd.yyyy (US style).</summary>
    MonthDayYear = 2
}
