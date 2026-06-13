using System.IO;
using System.Text;
using GrampsWeb.Mcp.Models;

namespace GrampsWeb.Mcp.Formatters;

/// <summary>
/// Formats media API responses.
/// </summary>
public static class MediaFormatter
{
    private const string GallerySectionTitle = "Gallery (media)";

    /// <summary>Uses resolved <paramref name="extendedMedia"/> when present; otherwise handle bullets from <paramref name="handleFallback"/>.</summary>
    public static void AppendExtendedMediaSection(
        StringBuilder sb,
        GrampsMedia[]? extendedMedia,
        string[]? handleFallback)
    {
        if (extendedMedia is { Length: > 0 })
        {
            sb.AppendLine();
            sb.AppendLine($"{GallerySectionTitle} ({extendedMedia.Length}):");
            foreach (var m in extendedMedia)
            {
                var fn = Path.GetFileName(m.Path ?? "");
                var label = !string.IsNullOrWhiteSpace(m.Description)
                    ? m.Description.Trim()
                    : (!string.IsNullOrEmpty(fn) ? fn : "(media)");
                var mh = string.IsNullOrWhiteSpace(m.Handle) ? "—" : m.Handle.Trim();
                sb.AppendLine($"  • {label} [handle: {mh}]");
            }
            return;
        }

        HandleListFormatter.AppendHandleBulletSection(sb, GallerySectionTitle, handleFallback);
    }

    public static string FormatMediaFull(GrampsMedia media, IReadOnlyList<BacklinkGroup>? backlinks = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"MEDIA [handle: {media.Handle}] (gramps_id: {media.GrampsId})");
        sb.AppendLine(new string('=', 60));

        sb.AppendLine($"Path: {media.Path ?? "(not specified)"}");
        if (!string.IsNullOrEmpty(media.Mime))
            sb.AppendLine($"MIME: {media.Mime}");
        if (!string.IsNullOrEmpty(media.Checksum))
            sb.AppendLine($"Checksum: {media.Checksum}");
        if (!string.IsNullOrEmpty(media.Description))
            sb.AppendLine($"\nDescription:\n{media.Description}");

        HandleListFormatter.AppendHandleBulletSection(sb, "Citations", media.CitationList);
        HandleListFormatter.AppendHandleBulletSection(sb, "Notes", media.NoteList);
        HandleListFormatter.AppendHandleBulletSection(sb, "Tags", media.TagList);
        BacklinkFormatter.AppendReferencedBySections(sb, backlinks);
        if (media.Private)
            sb.AppendLine("⚠ Private record");

        return sb.ToString();
    }
}
