using System.Text;

namespace GrampsWeb.Mcp.Formatters;

/// <summary>
/// Human-readable kinship strings for tree listings (English).
/// </summary>
public static class KinshipLabels
{
    /// <summary>
    /// e.g. [true] → Father, [true,false] → Father's mother.
    /// </summary>
    public static string AncestorChainLabel(IReadOnlyList<bool> fatherSidePath)
    {
        if (fatherSidePath.Count == 0)
            return "";

        var parts = new string[fatherSidePath.Count];
        for (var i = 0; i < fatherSidePath.Count; i++)
            parts[i] = fatherSidePath[i] ? "father" : "mother";

        var s = string.Join("'s ", parts);
        return char.ToUpperInvariant(s[0]) + s.AsSpan(1).ToString();
    }

    /// <summary>
    /// Gramps gender: 0 female, 1 male, 2 unknown.
    /// <paramref name="generation"/> is 1 for children, 2 for grandchildren, etc.
    /// </summary>
    public static string DescendantKinshipLabel(int generation, int gender)
    {
        var male = gender == 1;
        var female = gender == 0;

        if (generation == 1)
            return male ? "Son" : female ? "Daughter" : "Child";

        if (generation == 2)
            return male ? "Grandson" : female ? "Granddaughter" : "Grandchild";

        // generation >= 3: Great-grandson, Great-great-grandson, ...
        var sb = new StringBuilder("Great");
        for (var i = 0; i < generation - 3; i++)
            sb.Append("-great");

        sb.Append(male ? "-grandson" : female ? "-granddaughter" : "-grandchild");
        return sb.ToString();
    }
}
