using System.Text;
using GrampsWeb.Mcp.Models;

namespace GrampsWeb.Mcp.Formatters;

/// <summary>
/// Formats note API responses.
/// </summary>
public static class NoteFormatter
{
    public static string FormatNoteFull(GrampsNote note)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"NOTE [handle: {note.Handle}] (gramps_id: {note.GrampsId})");
        sb.AppendLine(new string('=', 60));
        sb.AppendLine($"Type:   {note.Type ?? "General"}");
        sb.AppendLine($"Format: {(note.Format == 1 ? "Flowed (HTML)" : "Plain Text")}");
        sb.AppendLine();
        sb.AppendLine(note.Text ?? "(empty)");
        if (note.TagList?.Length > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"Tags: {string.Join(", ", note.TagList)}");
        }
        return sb.ToString();
    }
}
