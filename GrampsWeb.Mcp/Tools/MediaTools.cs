using System.ComponentModel;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Dates;
using GrampsWeb.Mcp.Formatters;
using GrampsWeb.Mcp.Input;
using GrampsWeb.Mcp.Models;
using GrampsWeb.Mcp.Requests;
using GrampsWeb.Mcp.Config;
using GrampsWeb.Mcp.Resources;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace GrampsWeb.Mcp.Tools;

/// <summary>
/// MCP tools for Media objects—images, audio, and other digital files: reading them, uploading photos and
/// scans from the server's upload folders, and changing their records.
/// </summary>
[McpServerToolType]
public static class MediaTools
{
    [Description(
        "Read-only: media object metadata (path, MIME, checksum, description). " +
        "For Open WebUI vision access, use read_media with mode thumbnail or file. " +
        "Full MCP clients may also read resources gramps://media/{handle}/thumbnail/{size} or gramps://media/{handle}/file.")]
    internal static async Task<string> ReadMediaAsync(
        [Description("Media handle. " + ToolDescriptionFragments.HandleDiscovery)]
        string handle,
        GrampsApiClient client)
    {
        try
        {
            var resolvedHandle = await HandleResolver.ResolveToHandleAsync(handle, client, "media");
            var media = await client.GetOrNullIfNotFoundAsync<GrampsMedia>(
                $"/api/media/{Uri.EscapeDataString(resolvedHandle)}");
            if (media == null)
                return NotFoundHelper.NotFoundMessage("Media", handle);
            var backlinks = await BacklinkCollector.CollectAsync(client, "media", resolvedHandle);
            return await MediaFormatter.FormatMediaFull(media, client, backlinks);
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

    [McpServerTool(Title = "Read Media", ReadOnly = true, Destructive = false)]
    [Description(
        "Download a media file's bytes so you can see it. Default mode thumbnail renders a JPEG preview (PNG when transparent) from an image original " +
        "as MCP image content, 1568 pixels on the long edge unless size is given; EXIF and other metadata are stripped. " +
        "Prefer it for reading photos and document scans; region shows one part of an image, as a media link with that region does. " +
        "Use mode file only when the original is needed: returns image, audio, " +
        "or embedded blob resource content according to MIME type. " +
        "Requires GRAMPS_MEDIA_RESOURCES_ENABLED=true and respects size and private-record safeguards. " +
        "For the media record itself (path, description, links) use get_object; to change it use update_media.")]
    public static async Task<CallToolResult> ReadMedia(
        [Description("The media object to show. " + ToolDescriptionFragments.HandleDiscovery)]
        string handle,
        [Description("Download mode: thumbnail | file. Default thumbnail. Use file only when a preview is insufficient.")]
        string mode = "thumbnail",
        [Description("Thumbnail long edge in pixels, 1 to 4096; default 1568, which keeps handwriting and small print legible. " +
                     "Use 512 or less for a quick look at photos. Images are never upscaled. Only valid for mode thumbnail; omit for mode file.")]
        int? size = null,
        GrampsApiClient client = null!,
        GrampsConfig config = null!,
        [Description("Show only this part of the image, at up to size pixels on its long edge: [left, top, right, bottom] in percent of the image " +
                     "width and height, 0 to 100, from the top-left corner, as stored on a media link. Use it to check a region before saving it " +
                     "in mediaHandles or attachTo, or to read one record on a page scan. Only valid for mode thumbnail.")]
        double[]? region = null)
    {
        try
        {
            GrampsResources.EnsureMediaResourcesEnabled(config);
            GrampsResources.EnsureMediaHandle(handle);
            var normalizedMode = mode?.Trim().ToLowerInvariant();
            if (normalizedMode is not ("thumbnail" or "file"))
                throw McpToolErrors.ValidationError("Invalid mode. Must be thumbnail or file.");
            if (normalizedMode == "file" && size.HasValue)
                throw McpToolErrors.ValidationError("size is only supported for mode thumbnail; omit it for mode file.");
            if (normalizedMode == "thumbnail" && size.HasValue)
                MediaPreviewRenderer.EnsureValidSize(size.Value);
            if (normalizedMode == "file" && region != null)
                throw McpToolErrors.ValidationError("region is only supported for mode thumbnail; omit it for mode file.");
            MediaRegion? crop = null;
            if (region != null)
            {
                if (!MediaRegion.TryCreate(region, out crop, out var regionError))
                    throw McpToolErrors.ValidationError($"region: {regionError} {MediaRegion.DescriptionHint}");
                if (crop.IsWholeImage)
                    crop = null;
            }

            var resolvedHandle = await HandleResolver.ResolveToHandleAsync(handle, client, "media");
            if (normalizedMode == "file")
            {
                var mediaFile = await GrampsResources.DownloadMediaFileAsync(resolvedHandle, client, config);
                return GrampsResources.ToMediaFileCallToolResult(mediaFile);
            }
            var thumbnail = await GrampsResources.RenderMediaThumbnailAsync(
                resolvedHandle, size ?? MediaPreviewRenderer.DefaultSize, client, config, crop);
            return new CallToolResult { Content = [ImageContentBlock.FromBytes(thumbnail.Binary.Bytes, thumbnail.MimeType)] };
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

    private const string AttachToDescription =
        "Records to show the media on, each getting a link after its existing media: Gramps IDs (I0001, F0001, E0001, P0001, S0001, C0001) " +
        "or handles of people, families, events, places, sources or citations. JSON array, one value, or a comma-separated string. " +
        "To show only part of the image, such as one record on a page scan, give an object {\"ref\":\"I0001\",\"region\":[left, top, right, bottom]} " +
        "in place of the ID. " + MediaRegion.DescriptionHint + " A record already showing the media keeps its link and takes the region; " +
        "list a record twice to show two regions. Check a region with read_media first.";

    private const string CreateRetry =
        "Run create_media again with the same file and attachTo: it finds the stored media by checksum and adds only the missing links.";

    /// <summary>The records Gramps lets show media, by Gramps ID prefix.</summary>
    private static readonly MediaOwnerType[] OwnerTypes =
    [
        new('I', "person", "people"),
        new('F', "family", "families"),
        new('E', "event", "events"),
        new('P', "place", "places"),
        new('S', "source", "sources"),
        new('C', "citation", "citations"),
    ];

    [McpServerTool(Title = "Create Media", ReadOnly = false, Destructive = false)]
    [Description(
        "Upload a photo, document scan or PDF from the server's upload folder as a new media record, and show it on " +
        "people, families, events, places, sources or citations with attachTo, whole or as a region such as one record on a page scan. " +
        "Only files inside GRAMPS_MEDIA_UPLOAD_DIRS can be uploaded; the type (" + MediaUploadFile.SupportedTypes + ") is judged by content. " +
        "If the tree already stores an identical file (same MD5 checksum), nothing is uploaded: that record is returned with action existing, " +
        "its fields stay as they are and only attachTo is applied, so repeating a call is safe. " +
        "Returns the handle and Gramps ID; an unreadable date or unknown attachTo record is rejected before anything is uploaded. " +
        "Change the record later with update_media. " +
        ToolDescriptionFragments.WriteAccess + " " + ToolDescriptionFragments.InputGuide)]
    public static async Task<string> CreateMedia(
        [Description("The file to upload: an absolute path inside an upload folder, ~/…, or a name relative to the folder, e.g. scan.jpg.")]
        string filePath,
        [Description("Description (caption). Default: the file name without its extension.")]
        string? description = null,
        [Description("Date of the photo or document. " + ToolDescriptionFragments.DateText)]
        string? date = null,
        [Description(AttachToDescription)]
        FlexibleMediaLinkList? attachTo = null,
        [Description("Citations for the media itself. " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? citationHandles = null,
        [Description("Notes. " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? noteHandles = null,
        [Description("Tags. " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? tagHandles = null,
        [Description("Attributes. " + FlexibleAttributeList.DescriptionHint)]
        FlexibleAttributeList? attributes = null,
        [Description("Mark the media private (default false).")]
        bool isPrivate = false,
        GrampsApiClient client = null!,
        GrampsConfig config = null!)
    {
        try
        {
            var file = await MediaUploadFile.ReadAsync(filePath, config);
            var mediaDate = AgentDateParser.ToDateRequestOrNull(date, DateComponentOrder.Iso, DateIntervalPreference.Range);
            await KnownTypes.CheckAttributesAsync(attributes, client);
            using var updateLease = await client.BeginUpdateAsync();
            var owners = await ResolveOwnersAsync(attachTo, client, "Nothing was uploaded.");

            var existing = await FindByChecksumAsync(file.Checksum, client);
            if (existing?.Handle is { Length: > 0 } existingHandle)
            {
                var linked = await AttachAsync(owners, existingHandle, existing.GrampsId, client, CreateRetry);
                return ResponseEnvelope.FoundExisting("Media", existingHandle, existing.GrampsId, existing.Description,
                    $"{Path.GetFileName(file.Path)} is already stored as this media record (same MD5 checksum), so nothing was uploaded. " +
                    "Its description, date, notes, tags, citations, attributes and privacy were left as they are; change them with update_media." +
                    (owners.Count > 0 ? " attachTo was applied." : ""),
                    [new("mime", ResponseEnvelope.HeaderValue(existing.Mime)), new("attached_to", ResponseEnvelope.HeaderValue(linked))]);
            }

            var (handle, grampsId) = await UploadAsync(() => client.PostMediaFileAsync(file.Bytes, file.MimeType), file);
            if (string.IsNullOrWhiteSpace(handle))
                throw new McpException(
                    $"{Path.GetFileName(file.Path)} was uploaded, but Gramps Web returned no handle for the new media record. " +
                    "Run create_media again with the same file: it finds the stored record by checksum instead of uploading it twice.");

            var mediaPath = $"/api/media/{Uri.EscapeDataString(handle)}";
            var mediaDescription = string.IsNullOrWhiteSpace(description) ? file.DefaultDescription : description;
            try
            {
                var media = await GrampsObjectPatch.LoadAsync(client, mediaPath)
                    ?? throw new InvalidOperationException("Gramps Web did not find the record it had just created.");
                media.Set("desc", mediaDescription);
                media.Set("date", mediaDate);
                media.ReplaceAttributes(attributes);
                media.ApplyHandles("citation_list", citationHandles, "replace");
                media.ApplyHandles("note_list", noteHandles, "replace");
                media.ApplyHandles("tag_list", tagHandles, "replace");
                if (isPrivate)
                    media.Set("private", true);
                await media.SaveAsync(client);
                grampsId = media.GrampsId ?? grampsId;
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException(
                    $"{Path.GetFileName(file.Path)} was uploaded as media {grampsId ?? handle}, but saving its description and links failed: {ex.Message} " +
                    $"Do not upload it again: set them with update_media(handle: \"{handle}\").", ex);
            }

            var attached = await AttachAsync(owners, handle, grampsId, client, CreateRetry);
            return ResponseEnvelope.CreateSuccess("Media", handle, grampsId, mediaDescription,
                owners.Count == 0 ? ResponseEnvelope.MediaCreateNextSteps(handle) : null,
                [new("mime", ResponseEnvelope.HeaderValue(file.MimeType)), new("attached_to", ResponseEnvelope.HeaderValue(attached))]);
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

    [McpServerTool(Title = "Update Media", ReadOnly = false, Destructive = false)]
    [Description(
        "Change an existing media record's description, date, attributes or links to notes, tags and citations, " +
        "or replace its file with filePath from the server's upload folder (GRAMPS_MEDIA_UPLOAD_DIRS). " +
        "attachTo shows the media on people, families, events, places, sources or citations, whole or as a region; " +
        "to take it off a record, use that record's update tool with mediaHandles and linkMode remove. " +
        "For a new media record use create_media; to remove one, delete_object. " +
        ToolDescriptionFragments.UpdateSemantics + " " +
        "Returns the handle and Gramps ID; a missing media object returns a not-found message, and an unreadable date or file is rejected before anything is saved. " +
        ToolDescriptionFragments.WriteAccess + " " + ToolDescriptionFragments.InputGuide)]
    public static async Task<string> UpdateMedia(
        [Description("The media object to change. " + ToolDescriptionFragments.HandleDiscovery)]
        string handle,
        [Description("New description (caption). " + ToolDescriptionFragments.OmitToKeepScalar)]
        string? description = null,
        [Description("New date. Omit to keep the current date; pass an empty string to remove it. " + ToolDescriptionFragments.DateText)]
        string? date = null,
        [Description("Notes. " + ToolDescriptionFragments.LinkListOnUpdate + " " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? noteHandles = null,
        [Description("Tags. " + ToolDescriptionFragments.LinkListOnUpdate + " " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? tagHandles = null,
        [Description("Citations. " + ToolDescriptionFragments.LinkListOnUpdate + " " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? citationHandles = null,
        [Description("Attributes. " + ToolDescriptionFragments.ReplacedListOnUpdate + " " + FlexibleAttributeList.DescriptionHint)]
        FlexibleAttributeList? attributes = null,
        [Description("true makes the record private, false public. " + ToolDescriptionFragments.OmitToKeepScalar)]
        bool? isPrivate = null,
        GrampsApiClient client = null!,
        [Description(LinkUpdates.Description)]
        string linkMode = "replace",
        [Description("Replace the stored file with this one: an absolute path inside an upload folder, ~/…, or a name relative to the folder. " +
                     "Accepts " + MediaUploadFile.SupportedTypes + ", judged by content; a file another media record already holds is rejected. Omit to keep the file.")]
        string? filePath = null,
        GrampsConfig config = null!,
        [Description(AttachToDescription + " Not affected by linkMode.")]
        FlexibleMediaLinkList? attachTo = null)
    {
        try
        {
            LinkUpdates.Validate(linkMode);
            var file = filePath is null ? null : await MediaUploadFile.ReadAsync(filePath, config);
            var mediaDate = date is null ? null : AgentDateParser.ToDateRequestOrNull(date, DateComponentOrder.Iso, DateIntervalPreference.Range);
            await KnownTypes.CheckAttributesAsync(attributes, client);
            using var updateLease = await client.BeginUpdateAsync();
            var resolvedHandle = await HandleResolver.ResolveToHandleAsync(handle, client, "media");
            var mediaPath = $"/api/media/{Uri.EscapeDataString(resolvedHandle)}";
            var owners = await ResolveOwnersAsync(attachTo, client, "Nothing was changed.");
            var metadataChanges = HasMetadataChanges(description, date, noteHandles, tagHandles, citationHandles, attributes, isPrivate);

            List<KeyValuePair<string, string>> stored = [];
            GrampsMedia? current = null;
            if (file is not null || !metadataChanges)
            {
                current = await client.GetOrNullIfNotFoundAsync<GrampsMedia>(mediaPath);
                if (current == null)
                    return NotFoundHelper.NotFoundMessage("Media", handle);
            }

            if (file is not null && current is not null)
            {
                var replaced = false;
                if (!string.Equals(current.Checksum, file.Checksum, StringComparison.OrdinalIgnoreCase))
                {
                    var holder = await FindByChecksumAsync(file.Checksum, client);
                    if (holder is not null && holder.Handle != current.Handle)
                        throw McpToolErrors.ValidationError(
                            $"{Path.GetFileName(file.Path)} is already stored as media {holder.GrampsId ?? holder.Handle}; " +
                            "link that record instead of storing the file twice. Nothing was changed.");
                    replaced = await UploadAsync(() => client.PutMediaFileAsync(resolvedHandle, file.Bytes, file.MimeType), file);
                }
                stored.Add(new("file", replaced ? "replaced" : "unchanged (identical)"));
                stored.Add(new("mime", ResponseEnvelope.HeaderValue(file.MimeType)));
            }

            var (savedHandle, savedId) = (current?.Handle ?? resolvedHandle, current?.GrampsId);
            if (metadataChanges)
            {
                // Loaded after the file is replaced, so the save keeps the new checksum, path and MIME type.
                var media = await GrampsObjectPatch.LoadAsync(client, mediaPath);
                if (media == null)
                    return NotFoundHelper.NotFoundMessage("Media", handle);

                media.Set("desc", description);
                if (date != null)
                    media.SetOrRemove("date", mediaDate);
                media.ReplaceAttributes(attributes);
                media.ApplyHandles("citation_list", citationHandles, linkMode);
                media.ApplyHandles("note_list", noteHandles, linkMode);
                media.ApplyHandles("tag_list", tagHandles, linkMode);
                media.Set("private", isPrivate);

                await media.SaveAsync(client);
                (savedHandle, savedId) = (media.Handle ?? savedHandle, media.GrampsId ?? savedId);
            }

            if (owners.Count > 0)
            {
                var attached = await AttachAsync(owners, savedHandle, savedId, client,
                    $"Run update_media(handle: \"{savedHandle}\") again with the same attachTo: it adds only the missing links.");
                stored.Add(new("attached_to", ResponseEnvelope.HeaderValue(attached)));
            }

            return ResponseEnvelope.UpdateSuccess("Media", savedHandle, savedId, stored.Count > 0 ? stored : null);
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

    private static bool HasMetadataChanges(string? description, string? date, FlexibleHandleList? noteHandles,
        FlexibleHandleList? tagHandles, FlexibleHandleList? citationHandles, FlexibleAttributeList? attributes, bool? isPrivate) =>
        description != null || date != null || noteHandles != null || tagHandles != null
        || citationHandles != null || attributes != null || isPrivate != null;

    /// <summary>A stored media record whose file has this MD5 checksum, or null.</summary>
    private static async Task<GrampsMedia?> FindByChecksumAsync(string checksum, GrampsApiClient client)
    {
        var gql = Uri.EscapeDataString($"checksum = \"{checksum}\"");
        var found = await client.GetPagedListAsync<GrampsMedia>($"/api/media/?gql={gql}&page=1&pagesize=1");
        return found.Objects?.FirstOrDefault();
    }

    /// <summary>Runs an upload, explaining a timeout: Gramps Web may have stored the file all the same.</summary>
    private static async Task<T> UploadAsync<T>(Func<Task<T>> upload, MediaUploadFile file)
    {
        try
        {
            return await upload();
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            throw new McpException(
                $"Uploading {Path.GetFileName(file.Path)} ({file.Bytes.Length} bytes) did not finish in time; Gramps Web may have stored it anyway. " +
                "Run the same call again: an identical stored file is found by checksum instead of being uploaded twice.", ex);
        }
    }

    /// <summary>
    /// Finds each attachTo record before anything is uploaded or saved, so a typo costs nothing.
    /// Gramps IDs are looked up by their prefix; a handle is tried as each record type in turn.
    /// <paramref name="nothingDone"/> ends the not-found errors.
    /// </summary>
    private static async Task<List<MediaAttachment>> ResolveOwnersAsync(MediaLink[]? links, GrampsApiClient client, string nothingDone)
    {
        FlexibleMediaLinkList.EnsureValid(links, "attachTo");
        var attachments = new List<MediaAttachment>();
        var resolved = new Dictionary<string, MediaOwner>(StringComparer.Ordinal);
        foreach (var link in links ?? [])
        {
            var value = link.Ref?.Trim() ?? "";
            if (value.Length == 0)
                continue;
            if (!resolved.TryGetValue(value, out var owner))
            {
                owner = await ResolveOwnerAsync(value, client, nothingDone);
                resolved[value] = owner;
            }

            if (!attachments.Any(a => a.Owner.Handle == owner.Handle && a.Region == link.Region))
                attachments.Add(new MediaAttachment(owner, link.Region));
        }

        return attachments;
    }

    private static async Task<MediaOwner> ResolveOwnerAsync(string value, GrampsApiClient client, string nothingDone)
    {
        if (HandleResolver.LooksLikeGrampsId(value))
        {
            var type = OwnerTypes.FirstOrDefault(t => t.Prefix == value[0])
                ?? throw McpToolErrors.ValidationError(
                    $"attachTo: {value} is not a person, family, event, place, source or citation; media can be shown only on those. {nothingDone}");
            var resolved = await HandleResolver.ResolveToHandleAsync(value, client, type.Endpoint);
            return await LoadOwnerAsync(type, resolved, client)
                ?? throw McpToolErrors.ValidationError($"attachTo: there is no {type.Kind} {value}. {nothingDone}");
        }

        if (HandleResolver.LooksLikeHandle(value))
        {
            foreach (var type in OwnerTypes)
            {
                if (await LoadOwnerAsync(type, value, client) is { } owner)
                    return owner;
            }

            throw McpToolErrors.ValidationError(
                $"attachTo: no person, family, event, place, source or citation has handle {value}. {nothingDone}");
        }

        throw McpToolErrors.ValidationError($"attachTo: \"{value}\" is neither a Gramps ID such as I0001 nor a handle. {nothingDone}");
    }

    private static async Task<MediaOwner?> LoadOwnerAsync(MediaOwnerType type, string handle, GrampsApiClient client)
    {
        var path = $"/api/{type.Endpoint}/{Uri.EscapeDataString(handle)}";
        var stored = await client.GetJsonOrNullIfNotFoundAsync(path);
        if (stored is null)
            return null;
        var grampsId = stored.Value.TryGetProperty("gramps_id", out var id) && id.ValueKind == JsonValueKind.String
            ? id.GetString()
            : null;
        return new MediaOwner(type, path, handle, grampsId);
    }

    /// <summary>
    /// Adds the media after each owner's existing media (<see cref="GrampsObjectPatch.ApplyMediaLinks"/>, add mode),
    /// saving only owners whose links change. Returns the owners' Gramps IDs (handles when an ID is missing),
    /// each followed by the region it shows. <paramref name="retry"/> ends the error when an owner fails.
    /// </summary>
    private static async Task<List<string>> AttachAsync(
        IReadOnlyList<MediaAttachment> attachments, string mediaHandle, string? mediaGrampsId, GrampsApiClient client, string retry)
    {
        var attached = new List<string>();
        foreach (var group in attachments.GroupBy(a => a.Owner.Handle, StringComparer.Ordinal))
        {
            var owner = group.First().Owner;
            try
            {
                var patch = await GrampsObjectPatch.LoadAsync(client, owner.Path)
                    ?? throw new InvalidOperationException($"the {owner.Type.Kind} no longer exists.");
                var before = patch.Root["media_list"]?.ToJsonString();
                patch.ApplyMediaLinks(group.Select(a => new MediaLink(mediaHandle, a.Region)).ToArray(), "add", "attachTo");
                if (patch.Root["media_list"]?.ToJsonString() != before)
                    await patch.SaveAsync(client);
                attached.AddRange(group.Select(a => a.Label));
            }
            catch (Exception ex) when (ex is not McpException)
            {
                var done = attached.Count > 0 ? $" and shown on {string.Join(", ", attached)}" : "";
                throw new McpException(
                    $"Media {mediaGrampsId ?? mediaHandle} is stored{done}, but showing it on {owner.Type.Kind} {owner.Label} failed: {ex.Message} {retry}", ex);
            }
        }

        return attached;
    }

    private sealed record MediaOwnerType(char Prefix, string Kind, string Endpoint);

    private sealed record MediaOwner(MediaOwnerType Type, string Path, string Handle, string? GrampsId)
    {
        public string Label => GrampsId ?? Handle;
    }

    /// <summary>One attachTo entry: the record, and the region it shows (null keeps a stored region).</summary>
    private sealed record MediaAttachment(MediaOwner Owner, MediaRegion? Region)
    {
        public string Label => Region switch
        {
            null => Owner.Label,
            { IsWholeImage: true } => $"{Owner.Label} (whole image)",
            _ => $"{Owner.Label} region {Region}"
        };
    }
}
