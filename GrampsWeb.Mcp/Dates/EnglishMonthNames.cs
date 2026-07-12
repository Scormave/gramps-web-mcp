namespace GrampsWeb.Mcp.Dates;

/// <summary>
/// English month names shared by date formatting (abbreviated output) and agent date parsing.
/// </summary>
public static class EnglishMonthNames
{
    /// <summary>Index 1–12: Jan…Dec (empty at 0). Used for display.</summary>
    public static readonly string[] Abbreviated =
    {
        "", "Jan", "Feb", "Mar", "Apr", "May", "Jun",
        "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"
    };

    private static readonly Dictionary<string, int> Lookup = BuildLookup();

    /// <summary>
    /// Resolves an English abbreviated or full month name to 1–12 (case-insensitive).
    /// </summary>
    public static bool TryParse(string? token, out int month)
    {
        month = 0;
        if (string.IsNullOrWhiteSpace(token))
            return false;

        return Lookup.TryGetValue(token.Trim(), out month);
    }

    private static Dictionary<string, int> BuildLookup()
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        string[] full =
        {
            "", "January", "February", "March", "April", "May", "June",
            "July", "August", "September", "October", "November", "December"
        };

        for (var i = 1; i <= 12; i++)
        {
            map[Abbreviated[i]] = i;
            map[full[i]] = i;
        }

        // Common alternate abbreviations agents may echo
        map["Sept"] = 9;

        return map;
    }
}
