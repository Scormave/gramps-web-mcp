using System.Text;

namespace GrampsWeb.Mcp.Formatters;

/// <summary>
/// Renders lists of Gramps handles as bullet lines for <c>get_*</c> tool output.
/// </summary>
public static class HandleListFormatter
{
    /// <summary>
    /// Appends a blank line, a titled header with count, then <c>  • [handle: …]</c> per non-empty entry,
    /// named from <paramref name="labels"/> (<see cref="LinkedObjectLabels"/>) when it has the handle.
    /// </summary>
    public static void AppendHandleBulletSection(
        StringBuilder sb,
        string title,
        string[]? handles,
        IReadOnlyDictionary<string, string>? labels = null)
    {
        if (handles == null || handles.Length == 0)
            return;

        var items = handles.Where(h => !string.IsNullOrWhiteSpace(h)).Select(h => h.Trim()).ToList();
        if (items.Count == 0)
            return;

        sb.AppendLine();
        sb.AppendLine($"{title} ({items.Count}):");
        foreach (var h in items)
            sb.AppendLine(FormatBullet(h, labels));
    }

    /// <summary><c>  • label [handle: …]</c>, or <c>  • [handle: …]</c> when <paramref name="labels"/> does not name it.</summary>
    internal static string FormatBullet(string handle, IReadOnlyDictionary<string, string>? labels) =>
        labels?.GetValueOrDefault(handle) is { } label
            ? $"  • {label} [handle: {handle}]"
            : $"  • [handle: {handle}]";
}
