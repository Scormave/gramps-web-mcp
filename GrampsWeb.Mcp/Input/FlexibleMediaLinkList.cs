using System.Text.Json.Serialization;
using GrampsWeb.Mcp.Models;
using GrampsWeb.Mcp.Serialization;
using GrampsWeb.Mcp.Tools;

namespace GrampsWeb.Mcp.Input;

/// <summary>
/// MCP tool parameter for media links that may show only part of an image. Accepts the shapes of
/// <see cref="FlexibleHandleList"/>, and objects <c>{"ref": …, "region": [left, top, right, bottom]}</c>.
/// An unreadable region is kept in <see cref="MediaLink.RegionError"/> for the tool to report, since the
/// SDK does not pass converter messages on.
/// </summary>
[JsonConverter(typeof(FlexibleMediaLinkListJsonConverter))]
public sealed class FlexibleMediaLinkList
{
    /// <summary>Human-readable hint for MCP tool parameter descriptions.</summary>
    public const string DescriptionHint =
        "Media handles, not Gramps IDs: JSON array [\"h1\",\"h2\"], one handle, or a comma/semicolon/newline-separated string. " +
        "To show only part of an image, such as one record on a page scan, give an object {\"ref\":\"…\",\"region\":[left, top, right, bottom]} " +
        "in place of the handle. " + MediaRegion.DescriptionHint + " [0, 0, 100, 100] shows the whole image again; " +
        "read_media with the same region shows what it covers.";

    /// <summary>How linkMode treats regions, for update tools.</summary>
    public const string UpdateHint =
        "A plain handle keeps the region its link has. In add mode a media already shown keeps its link and takes the region given; " +
        "to show two regions of one image, list it twice, each with its region.";

    public required MediaLink[] Items { get; init; }

    public static implicit operator MediaLink[]?(FlexibleMediaLinkList? value) => value?.Items;

    /// <summary>Rejects the first link whose region could not be read, naming <paramref name="argument"/>.</summary>
    internal static void EnsureValid(MediaLink[]? links, string argument)
    {
        if (links?.FirstOrDefault(link => link.RegionError is not null) is { } bad)
            throw McpToolErrors.ValidationError($"{argument}: {bad.Ref}: {bad.RegionError} {MediaRegion.DescriptionHint}");
    }
}

/// <summary>
/// One link from a tool argument: a handle or Gramps ID, and the region of the image to show.
/// A null <see cref="Region"/> leaves a stored region as it is.
/// </summary>
public sealed record MediaLink(string Ref, MediaRegion? Region = null, string? RegionError = null);
