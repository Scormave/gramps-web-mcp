using System.Text;

namespace GrampsWeb.Mcp.Formatters;

/// <summary>
/// Renders lists of Gramps handles as bullet lines for <c>get_*</c> tool output.
/// </summary>
public static class HandleListFormatter
{
    /// <summary>Appends a blank line, a titled header with count, then <c>  • [handle: …]</c> per non-empty entry.</summary>
    public static void AppendHandleBulletSection(StringBuilder sb, string title, string[]? handles)
    {
        if (handles == null || handles.Length == 0)
            return;

        var items = handles.Where(h => !string.IsNullOrWhiteSpace(h)).Select(h => h.Trim()).ToList();
        if (items.Count == 0)
            return;

        sb.AppendLine();
        sb.AppendLine($"{title} ({items.Count}):");
        foreach (var h in items)
            sb.AppendLine($"  • [handle: {h}]");
    }
}
