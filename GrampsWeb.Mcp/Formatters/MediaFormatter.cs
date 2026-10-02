using System.IO;
using System.Text;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Dates;
using GrampsWeb.Mcp.Resources;
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

    public static async Task<string> FormatMediaFull(
        GrampsMedia media,
        GrampsApiClient client,
        IReadOnlyList<BacklinkGroup>? backlinks = null)
    {
        var labels = await LinkedObjectLabels.LoadAsync(client,
            [("citations", media.CitationList), ("notes", media.NoteList), ("tags", media.TagList)],
            backlinks);
        var sb = new StringBuilder();
        sb.AppendLine($"MEDIA [handle: {media.Handle}] (gramps_id: {media.GrampsId})");
        sb.AppendLine(new string('=', 60));

        sb.AppendLine($"Path: {media.Path ?? "(not specified)"}");
        if (!string.IsNullOrEmpty(media.Mime))
            sb.AppendLine($"MIME: {media.Mime}");
        if (!string.IsNullOrEmpty(media.Checksum))
            sb.AppendLine($"Checksum: {media.Checksum}");
        if (!GrampsDateHelpers.IsEmpty(media.Date))
            sb.AppendLine($"Date: {GrampsValueFormatter.FormatDate(media.Date!)}");
        if (!string.IsNullOrEmpty(media.Description))
            sb.AppendLine($"\nDescription:\n{media.Description}");

        if (!string.IsNullOrWhiteSpace(media.Handle))
        {
            var escapedHandle = Uri.EscapeDataString(media.Handle.Trim());
            sb.AppendLine();
            sb.AppendLine("Binary access (requires GRAMPS_MEDIA_RESOURCES_ENABLED=true):");
            sb.AppendLine($"  file resource: gramps://media/{escapedHandle}/file");
            // Thumbnails are rendered only from images; an unknown MIME type is left to the file content.
            if (string.IsNullOrWhiteSpace(media.Mime) || MediaPreviewRenderer.CanRenderMime(media.Mime.Trim()))
            {
                sb.AppendLine($"  thumbnail resource: gramps://media/{escapedHandle}/thumbnail/{MediaPreviewRenderer.DefaultSize}");
                sb.AppendLine($"  read_media defaults to mode thumbnail (JPEG/PNG preview, {MediaPreviewRenderer.DefaultSize} px long edge); mode file returns image, audio, or embedded-resource content depending on MIME type");
            }
            else
            {
                sb.AppendLine($"  read_media needs mode file: thumbnails are rendered only from images, and this record is {media.Mime.Trim()}");
            }
        }

        HandleListFormatter.AppendHandleBulletSection(sb, "Citations", media.CitationList, labels);
        HandleListFormatter.AppendHandleBulletSection(sb, "Notes", media.NoteList, labels);
        HandleListFormatter.AppendHandleBulletSection(sb, "Tags", media.TagList, labels);
        BacklinkFormatter.AppendReferencedBySections(sb, backlinks, labels);
        if (media.Private)
            sb.AppendLine("⚠ Private record");

        return sb.ToString();
    }
}
