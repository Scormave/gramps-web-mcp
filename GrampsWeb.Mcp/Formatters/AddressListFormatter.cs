using System.Text;
using GrampsWeb.Mcp.Dates;
using GrampsWeb.Mcp.Models;

namespace GrampsWeb.Mcp.Formatters;

/// <summary>Shared address_list and urls rendering for people and repositories.</summary>
public static class AddressListFormatter
{
    public static void AppendAddressesSection(StringBuilder sb, GrampsAddress[]? list)
    {
        if (list is null || list.Length == 0)
            return;

        sb.AppendLine();
        sb.AppendLine($"Addresses ({list.Length}):");
        foreach (var a in list)
        {
            var parts = new List<string>();
            void add(string? s)
            {
                if (!string.IsNullOrWhiteSpace(s))
                    parts.Add(s.Trim());
            }

            add(a.Street);
            add(a.Locality);
            add(a.City);
            add(a.County);
            add(a.State);
            add(a.Postal);
            add(a.Country);

            var line = parts.Count > 0 ? string.Join(", ", parts) : "";
            if (string.IsNullOrEmpty(line) && string.IsNullOrWhiteSpace(a.Phone))
                line = "(empty address lines)";

            sb.AppendLine($"  • {line}");
            if (!string.IsNullOrWhiteSpace(a.Phone))
                sb.AppendLine($"    phone: {a.Phone.Trim()}");
            if (!GrampsDateHelpers.IsEmpty(a.Date))
                sb.AppendLine($"    resident as of: {GrampsValueFormatter.FormatDate(a.Date!)}");

            if (a.Private)
                sb.AppendLine("    ⚠ private");
            AppendIndentedHandleListLine(sb, "    citations", a.CitationList);
            AppendIndentedHandleListLine(sb, "    notes", a.NoteList);
        }
    }

    public static void AppendUrlsSection(StringBuilder sb, GrampsUrl[]? list)
    {
        if (list is null || list.Length == 0)
            return;

        sb.AppendLine();
        sb.AppendLine($"URLs ({list.Length}):");
        foreach (var u in list)
        {
            var typ = string.IsNullOrWhiteSpace(u.Type) ? "URL" : u.Type.Trim();
            var path = string.IsNullOrWhiteSpace(u.Path) ? "—" : u.Path.Trim();
            var desc = string.IsNullOrWhiteSpace(u.Description) ? "" : $" — {u.Description.Trim()}";
            var priv = u.Private ? " ⚠ private" : "";
            sb.AppendLine($"  • [{typ}] {path}{desc}{priv}");
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
