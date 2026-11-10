using System.Text;
using GrampsWeb.Mcp.Models;

namespace GrampsWeb.Mcp.Formatters;

/// <summary>Shared attribute_list rendering for person, family, event, etc.</summary>
public static class AttributeListFormatter
{
    public static void AppendSection(StringBuilder sb, GrampsAttribute[]? list)
    {
        if (list is null || list.Length == 0)
            return;

        sb.AppendLine();
        sb.AppendLine($"Attributes ({list.Length}):");
        foreach (var a in list)
        {
            var type = string.IsNullOrWhiteSpace(a.Type) ? "—" : a.Type.Trim();
            var val = string.IsNullOrWhiteSpace(a.Value) ? "—" : a.Value.Trim();
            var priv = a.Private ? " ⚠ private" : "";
            sb.AppendLine($"  • {type}: {val}{priv}");
            AppendIndentedHandleListLine(sb, "    citations", a.CitationList);
            AppendIndentedHandleListLine(sb, "    notes", a.NoteList);
        }
    }

    /// <summary>
    /// The attributes of an event or media link on one line, such as <c>Age: 65; Occupation: Farmer</c>, each with its
    /// privacy and its citation and note handles; null when there are none.
    /// </summary>
    public static string? FormatInline(GrampsAttribute[]? list)
    {
        if (list is null || list.Length == 0)
            return null;
        return string.Join("; ", list.Select(a =>
        {
            var type = string.IsNullOrWhiteSpace(a.Type) ? "—" : a.Type.Trim();
            var val = string.IsNullOrWhiteSpace(a.Value) ? "—" : a.Value.Trim();
            var priv = a.Private ? " ⚠ private" : "";
            return $"{type}: {val}{priv}{InlineHandles("citations", a.CitationList)}{InlineHandles("notes", a.NoteList)}";
        }));
    }

    private static string InlineHandles(string label, string[]? handles)
    {
        var cleaned = handles?.Where(h => !string.IsNullOrWhiteSpace(h)).Select(h => h.Trim()).ToArray() ?? [];
        return cleaned.Length == 0 ? "" : $" [{label}: {string.Join(", ", cleaned)}]";
    }

    private static void AppendIndentedHandleListLine(StringBuilder sb, string label, string[]? handles)
    {
        if (handles is null || handles.Length == 0)
            return;
        var cleaned = handles.Where(h => !string.IsNullOrWhiteSpace(h)).Select(h => h.Trim()).ToArray();
        if (cleaned.Length == 0)
            return;
        sb.AppendLine($"{label}: {string.Join(", ", cleaned.Select(h => $"[handle: {h}]"))}");
    }
}
