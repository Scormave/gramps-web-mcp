using GrampsWeb.Mcp.Tools;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace GrampsWeb.Mcp.Resources;

/// <summary>
/// Renders agent-readable previews from original media files. Gramps Web thumbnails are always
/// AVIF, which vision clients cannot read, so previews are decoded and downscaled locally.
/// </summary>
internal static class MediaPreviewRenderer
{
    /// <summary>Default long edge in pixels; keeps document scans legible for vision models.</summary>
    public const int DefaultSize = 1568;

    public const int MaxSize = 4096;

    /// <summary>
    /// Originals are downloaded up to this size, or up to GRAMPS_MEDIA_MAX_BYTES if that is larger.
    /// The limit protects memory; only the encoded preview is returned to the client.
    /// </summary>
    public const long SourceMaxBytes = 50L * 1024 * 1024;

    public const long MaxSourcePixels = 100_000_000;

    private const int JpegQuality = 85;

    private const string SupportedFormats = "JPEG, PNG, GIF, WebP, BMP, TIFF, TGA, PBM, or QOI";

    private const string NotRenderableMessage =
        "The media file is not an image the server can render. Supported originals: " + SupportedFormats +
        " (multi-page files use the first page). Use read_media with mode file for the original.";

    private static readonly HashSet<string> UnsupportedImageMimeTypes = new(StringComparer.Ordinal)
    {
        "image/avif",
        "image/heic",
        "image/heic-sequence",
        "image/heif",
        "image/heif-sequence",
        "image/jxl",
        "image/svg+xml"
    };

    public static void EnsureValidSize(int size)
    {
        if (size is < 1 or > MaxSize)
            throw McpToolErrors.ValidationError($"Thumbnail size must be an integer from 1 to {MaxSize} pixels.");
    }

    /// <summary>False for MIME types that are known not to produce a preview.</summary>
    public static bool CanRenderMime(string mimeType) =>
        mimeType.StartsWith("image/", StringComparison.Ordinal) && !UnsupportedImageMimeTypes.Contains(mimeType);

    /// <summary>
    /// Rejects media that cannot produce a preview before its original is downloaded.
    /// An empty MIME type is allowed; the file content decides.
    /// </summary>
    public static void EnsurePreviewableMime(string? mimeType)
    {
        if (mimeType == null)
            return;

        if (!mimeType.StartsWith("image/", StringComparison.Ordinal))
            throw McpToolErrors.ValidationError(
                $"Thumbnails are only available for image media; this record is '{mimeType}'. Use read_media with mode file for the original.");

        if (UnsupportedImageMimeTypes.Contains(mimeType))
            throw McpToolErrors.ValidationError(
                $"Thumbnails cannot be rendered from '{mimeType}' images. Supported originals: {SupportedFormats}. Use read_media with mode file for the original.");
    }

    /// <summary>
    /// Decodes the first frame, applies EXIF orientation, downscales to <paramref name="size"/> on the
    /// long edge without upscaling, and re-encodes from raw pixels so no source metadata (EXIF, GPS,
    /// XMP, IPTC, ICC, comments) survives. Output is PNG only when the image has transparent pixels.
    /// </summary>
    public static RenderedPreview Render(ReadOnlySpan<byte> source, int size, long maxSourcePixels = MaxSourcePixels)
    {
        EnsureValidSize(size);
        if (source.IsEmpty)
            throw McpToolErrors.ValidationError(NotRenderableMessage);

        try
        {
            var info = Image.Identify(source);
            if ((long)info.Width * info.Height > maxSourcePixels)
                throw McpToolErrors.ValidationError(
                    $"Image is {info.Width}x{info.Height} pixels, exceeding the preview limit of {maxSourcePixels / 1_000_000} megapixels. Use read_media with mode file for the original.");

            var options = new DecoderOptions
            {
                MaxFrames = 1,
                Sampler = KnownResamplers.Lanczos3,
                // Square bounds: EXIF orientation only swaps width and height.
                TargetSize = Math.Max(info.Width, info.Height) > size ? new Size(size, size) : null
            };

            using var image = Image.Load(options, source);
            image.Mutate(x => x.AutoOrient());
            if (image.Width > size || image.Height > size)
            {
                image.Mutate(x => x.Resize(new ResizeOptions
                {
                    Size = new Size(size, size),
                    Mode = ResizeMode.Max,
                    Sampler = KnownResamplers.Lanczos3
                }));
            }

            return Encode(image);
        }
        catch (Exception ex) when (ex is ImageFormatException or ImageProcessingException or NotSupportedException)
        {
            throw McpToolErrors.ValidationError(NotRenderableMessage);
        }
        catch (InvalidMemoryOperationException)
        {
            throw McpToolErrors.ValidationError(
                "The image is too large to render a preview. Use read_media with mode file for the original.");
        }
    }

    private static RenderedPreview Encode(Image image)
    {
        using var rgba = image.CloneAs<Rgba32>();
        var pixels = new Rgba32[rgba.Width * rgba.Height];
        rgba.CopyPixelDataTo(pixels);

        var transparent = false;
        foreach (var pixel in pixels)
        {
            if (pixel.A < byte.MaxValue)
            {
                transparent = true;
                break;
            }
        }

        // A fresh image carries no source metadata into the encoder.
        using var clean = Image.LoadPixelData<Rgba32>(pixels, rgba.Width, rgba.Height);
        using var output = new MemoryStream();
        if (transparent)
            clean.SaveAsPng(output);
        else
            clean.SaveAsJpeg(output, new JpegEncoder { Quality = JpegQuality });

        return new RenderedPreview(
            output.ToArray(),
            transparent ? "image/png" : "image/jpeg",
            clean.Width,
            clean.Height);
    }

    internal sealed record RenderedPreview(byte[] Bytes, string MimeType, int Width, int Height);
}
