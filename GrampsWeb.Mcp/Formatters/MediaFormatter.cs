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

    /// <summary>
    /// Uses resolved <paramref name="extendedMedia"/> when present, which Gramps lists in the order of
    /// <paramref name="links"/>; otherwise handle bullets from <paramref name="links"/>. Either way a link that shows
    /// part of its image is followed by its region.
    /// </summary>
    public static void AppendExtendedMediaSection(
        StringBuilder sb,
        GrampsMedia[]? extendedMedia,
        GrampsMediaRef[]? links)
    {
        if (extendedMedia is { Length: > 0 })
        {
            sb.AppendLine();
            sb.AppendLine($"{GallerySectionTitle} ({extendedMedia.Length}):");
            for (var i = 0; i < extendedMedia.Length; i++)
            {
                var m = extendedMedia[i];
                var fn = Path.GetFileName(m.Path ?? "");
                var label = !string.IsNullOrWhiteSpace(m.Description)
                    ? m.Description.Trim()
                    : (!string.IsNullOrEmpty(fn) ? fn : "(media)");
                var mh = string.IsNullOrWhiteSpace(m.Handle) ? "—" : m.Handle.Trim();
                var link = links is not null && i < links.Length && links[i].ResolvedRef == mh ? links[i] : null;
                sb.AppendLine($"  • {label} [handle: {mh}]{RegionSuffix(link?.Rect)}");
            }
            return;
        }

        AppendMediaLinkSection(sb, GallerySectionTitle, links);
    }

    /// <summary>
    /// Media links as handle bullets (<see cref="HandleListFormatter.FormatBullet"/>), each followed by
    /// <c>region [left, top, right, bottom]</c> when it shows only part of its image.
    /// </summary>
    public static void AppendMediaLinkSection(
        StringBuilder sb,
        string title,
        GrampsMediaRef[]? links,
        IReadOnlyDictionary<string, string>? labels = null)
    {
        var shown = links?.Where(link => link.ResolvedRef is not null).ToList();
        if (shown is not { Count: > 0 })
            return;

        sb.AppendLine();
        sb.AppendLine($"{title} ({shown.Count}):");
        foreach (var link in shown)
            sb.AppendLine(HandleListFormatter.FormatBullet(link.ResolvedRef!, labels) + RegionSuffix(link.Rect));
    }

    private static string RegionSuffix(int[]? rect) =>
        MediaRegion.FromStored(rect) is { } region ? $" region {region}" : "";

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
