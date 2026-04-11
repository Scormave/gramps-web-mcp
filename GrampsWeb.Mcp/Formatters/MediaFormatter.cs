using System.Text;
using GrampsWeb.Mcp.Models;

namespace GrampsWeb.Mcp.Formatters;

/// <summary>
/// Formats media API responses.
/// </summary>
public static class MediaFormatter
{
    public static string FormatMediaFull(GrampsMedia media)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"MEDIA [handle: {media.Handle}] (gramps_id: {media.GrampsId})");
        sb.AppendLine(new string('=', 60));

        sb.AppendLine($"Path: {media.Path ?? "(not specified)"}");
        if (!string.IsNullOrEmpty(media.Mime))
            sb.AppendLine($"MIME: {media.Mime}");
        if (!string.IsNullOrEmpty(media.Description))
            sb.AppendLine($"\nDescription:\n{media.Description}");

        HandleListFormatter.AppendHandleBulletSection(sb, "Citations", media.CitationList);
        HandleListFormatter.AppendHandleBulletSection(sb, "Notes", media.NoteList);
        HandleListFormatter.AppendHandleBulletSection(sb, "Tags", media.TagList);
        if (media.Private)
            sb.AppendLine("⚠ Private record");

        return sb.ToString();
    }
}
