using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;

namespace GrampsWeb.Mcp.Tests.UnitTests;

/// <summary>
/// Generates small encoded images for media preview tests.
/// </summary>
internal static class TestImages
{
    public static byte[] Jpeg(int width, int height, Action<Image>? configure = null)
    {
        using var image = new Image<Rgb24>(width, height, new Rgb24(180, 120, 60));
        configure?.Invoke(image);
        return Encode(stream => image.SaveAsJpeg(stream, new JpegEncoder { Quality = 90 }));
    }

    public static byte[] Png(int width, int height, bool transparent)
    {
        using var image = new Image<Rgba32>(width, height, new Rgba32(30, 90, 150, 255));
        if (transparent)
            image[0, 0] = new Rgba32(0, 0, 0, 0);
        return Encode(stream => image.SaveAsPng(stream));
    }

    public static byte[] Tiff(int width, int height)
    {
        using var image = new Image<Rgb24>(width, height, new Rgb24(60, 160, 90));
        return Encode(stream => image.SaveAsTiff(stream));
    }

    /// <summary>Random pixels at maximum quality, so the JPEG is far larger than its preview.</summary>
    public static byte[] NoisyJpeg(int width, int height)
    {
        var random = new Random(42);
        using var image = new Image<Rgb24>(width, height);
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
            image[x, y] = new Rgb24((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256));
        return Encode(stream => image.SaveAsJpeg(stream, new JpegEncoder { Quality = 100 }));
    }

    public static ImageInfo Identify(ReadOnlyMemory<byte> bytes) => Image.Identify(bytes.Span);

    private static byte[] Encode(Action<Stream> save)
    {
        using var stream = new MemoryStream();
        save(stream);
        return stream.ToArray();
    }
}
