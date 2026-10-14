using System.Text;
using GrampsWeb.Mcp.Models;
using GrampsWeb.Mcp.Resources;
using ModelContextProtocol;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.Metadata.Profiles.Iptc;
using SixLabors.ImageSharp.Metadata.Profiles.Xmp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

public class MediaPreviewRendererTests
{
    [Fact]
    public void Render_Downscales_Long_Edge_To_Requested_Size()
    {
        var preview = MediaPreviewRenderer.Render(TestImages.Jpeg(3000, 2000), 512);

        Assert.Equal("image/jpeg", preview.MimeType);
        Assert.Equal(512, preview.Width);
        Assert.InRange(preview.Height, 340, 342);
        var info = TestImages.Identify(preview.Bytes);
        Assert.Equal((preview.Width, preview.Height), (info.Width, info.Height));
    }

    [Fact]
    public void Render_Crops_A_Region_Before_Downscaling()
    {
        var source = TestImages.Jpeg(1000, 500, image =>
        {
            var pixels = (Image<Rgb24>)image;
            for (var y = 0; y < 500; y++)
            for (var x = 500; x < 1000; x++)
                pixels[x, y] = new Rgb24(0, 0, 255);
        });

        var preview = MediaPreviewRenderer.Render(source, 256, new MediaRegion(50, 0, 100, 100));

        Assert.Equal((256, 256), (preview.Width, preview.Height));
        using var image = Image.Load<Rgb24>(preview.Bytes);
        Assert.InRange(image[128, 128].B, 200, 255);
        Assert.InRange(image[2, 128].R, 0, 60);
    }

    [Fact]
    public void Render_Reads_A_Small_Region_At_Full_Resolution()
    {
        var preview = MediaPreviewRenderer.Render(TestImages.Jpeg(3000, 2000), 512, new MediaRegion(0, 0, 10, 10));

        Assert.Equal((300, 200), (preview.Width, preview.Height));
    }

    [Fact]
    public void Render_Region_Follows_Exif_Orientation()
    {
        var source = TestImages.Jpeg(300, 200, image =>
        {
            image.Metadata.ExifProfile = new ExifProfile();
            image.Metadata.ExifProfile.SetValue(ExifTag.Orientation, (ushort)6);
        });

        var preview = MediaPreviewRenderer.Render(source, 1568, new MediaRegion(0, 0, 100, 50));

        Assert.Equal((200, 150), (preview.Width, preview.Height));
    }

    [Theory]
    [InlineData(10, 10, 99, 99, 100, 100, 9, 9, 1, 1)]
    [InlineData(1000, 500, 10, 20, 60, 45, 100, 100, 500, 125)]
    [InlineData(3, 3, 0, 0, 1, 1, 0, 0, 1, 1)]
    public void CropRectangle_Covers_At_Least_One_Pixel(int width, int height, int left, int top, int right, int bottom,
        int x, int y, int cropWidth, int cropHeight)
    {
        var rectangle = MediaPreviewRenderer.CropRectangle(width, height, new MediaRegion(left, top, right, bottom));

        Assert.Equal(new Rectangle(x, y, cropWidth, cropHeight), rectangle);
    }

    [Fact]
    public void Render_Never_Upscales()
    {
        var preview = MediaPreviewRenderer.Render(TestImages.Jpeg(64, 48), MediaPreviewRenderer.DefaultSize);

        Assert.Equal((64, 48), (preview.Width, preview.Height));
    }

    [Theory]
    [InlineData(300, 200, 1568, 200, 300)]
    [InlineData(3000, 2000, 512, 341, 512)]
    public void Render_Applies_Exif_Orientation(int width, int height, int size, int expectedWidth, int expectedHeight)
    {
        var source = TestImages.Jpeg(width, height, image =>
        {
            image.Metadata.ExifProfile = new ExifProfile();
            image.Metadata.ExifProfile.SetValue(ExifTag.Orientation, (ushort)6);
        });

        var preview = MediaPreviewRenderer.Render(source, size);

        Assert.InRange(preview.Width, expectedWidth - 1, expectedWidth + 1);
        Assert.Equal(expectedHeight, preview.Height);
    }

    [Fact]
    public void Render_Strips_Source_Metadata()
    {
        var source = TestImages.Jpeg(200, 100, image =>
        {
            image.Metadata.ExifProfile = new ExifProfile();
            image.Metadata.ExifProfile.SetValue(ExifTag.Artist, "SecretArtist");
            image.Metadata.ExifProfile.SetValue(ExifTag.GPSLatitudeRef, "N");
            image.Metadata.IptcProfile = new IptcProfile();
            image.Metadata.IptcProfile.SetValue(IptcTag.Caption, "SecretCaption");
            image.Metadata.XmpProfile = new XmpProfile(Encoding.UTF8.GetBytes(
                "<x:xmpmeta xmlns:x=\"adobe:ns:meta/\"><s>SecretXmp</s></x:xmpmeta>"));
        });
        Assert.Contains("SecretArtist", Encoding.Latin1.GetString(source));

        var preview = MediaPreviewRenderer.Render(source, 64);

        var text = Encoding.Latin1.GetString(preview.Bytes);
        Assert.DoesNotContain("Secret", text);
        Assert.DoesNotContain("Exif", text);
        using var decoded = Image.Load(preview.Bytes);
        Assert.Null(decoded.Metadata.ExifProfile);
        Assert.Null(decoded.Metadata.IptcProfile);
        Assert.Null(decoded.Metadata.XmpProfile);
        Assert.Null(decoded.Metadata.IccProfile);
    }

    [Theory]
    [InlineData(true, "image/png")]
    [InlineData(false, "image/jpeg")]
    public void Render_Keeps_Png_Only_For_Transparent_Images(bool transparent, string expectedMimeType)
    {
        var preview = MediaPreviewRenderer.Render(TestImages.Png(40, 40, transparent), 32);

        Assert.Equal(expectedMimeType, preview.MimeType);
        Assert.Equal(expectedMimeType, TestImages.Identify(preview.Bytes).Metadata.DecodedImageFormat?.DefaultMimeType);
    }

    [Fact]
    public void Render_Reads_Tiff()
    {
        var preview = MediaPreviewRenderer.Render(TestImages.Tiff(120, 80), 60);

        Assert.Equal("image/jpeg", preview.MimeType);
        Assert.Equal((60, 40), (preview.Width, preview.Height));
    }

    [Theory]
    [InlineData(new byte[0])]
    [InlineData(new byte[] { 1, 2, 3 })]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10 })]
    public void Render_Rejects_Non_Image_Bytes(byte[] source)
    {
        var ex = Assert.Throws<McpException>(() => MediaPreviewRenderer.Render(source, 256));

        Assert.Contains("not an image the server can render", ex.Message);
    }

    [Fact]
    public void Render_Rejects_Images_Over_Pixel_Limit()
    {
        var ex = Assert.Throws<McpException>(
            () => MediaPreviewRenderer.Render(TestImages.Jpeg(64, 48), 32, maxSourcePixels: 3000));

        Assert.Contains("64x48 pixels", ex.Message);
        Assert.Contains("mode file", ex.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(MediaPreviewRenderer.MaxSize + 1)]
    public void EnsureValidSize_Rejects_Out_Of_Range(int size)
    {
        var ex = Assert.Throws<McpException>(() => MediaPreviewRenderer.EnsureValidSize(size));

        Assert.Contains("from 1 to 4096", ex.Message);
    }

    [Theory]
    [InlineData("image/jpeg", true)]
    [InlineData("image/tiff", true)]
    [InlineData("image/x-portable-anymap", true)]
    [InlineData("image/avif", false)]
    [InlineData("image/heic", false)]
    [InlineData("image/svg+xml", false)]
    [InlineData("application/pdf", false)]
    [InlineData("audio/aac", false)]
    public void CanRenderMime_Excludes_Known_Unsupported_Types(string mimeType, bool expected)
    {
        Assert.Equal(expected, MediaPreviewRenderer.CanRenderMime(mimeType));
    }
}
