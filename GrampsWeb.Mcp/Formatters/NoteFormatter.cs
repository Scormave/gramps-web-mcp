using System.Text;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Models;

namespace GrampsWeb.Mcp.Formatters;

/// <summary>
/// Formats note API responses.
/// </summary>
public static class NoteFormatter
{
    public static async Task<string> FormatNoteFullAsync(GrampsNote note, GrampsApiClient client)
    {
        var typeLabel = string.IsNullOrWhiteSpace(note.Type)
            ? "General"
            : await GrampsDefaultTypeLabels.FormatNoteTypeAsync(client, note.Type);
        var sb = new StringBuilder();
        sb.AppendLine($"NOTE [handle: {note.Handle}] (gramps_id: {note.GrampsId})");
        sb.AppendLine(new string('=', 60));
        sb.AppendLine($"Type:   {typeLabel}");
        sb.AppendLine($"Format: {(note.Format == 1 ? "Flowed (HTML)" : "Plain Text")}");
        sb.AppendLine();
        sb.AppendLine(note.Text ?? "(empty)");
        HandleListFormatter.AppendHandleBulletSection(sb, "Tags", note.TagList);
        return sb.ToString();
    }
}
