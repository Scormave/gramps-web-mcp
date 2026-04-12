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
