using System.Net;
using System.Text;
using System.Text.Json;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Config;
using GrampsWeb.Mcp.Resources;
using GrampsWeb.Mcp.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

[Collection("HandleCache")]
public class MediaResourceTests
{
    private static readonly byte[] SmallJpeg = TestImages.Jpeg(64, 48);
    private static readonly byte[] LargeJpeg = TestImages.Jpeg(3000, 2000);
    private static readonly byte[] SmallTiff = TestImages.Tiff(120, 80);
    private static readonly byte[] NoisyJpeg = TestImages.NoisyJpeg(600, 600);

    [Fact]
    public async Task GetMediaThumbnail_Renders_Preview_From_Original()
    {
        var handler = new MediaHandler();
        var client = CreateClient(handler);
        var config = CreateConfig(mediaResourcesEnabled: true);

        var resource = await GrampsResources.GetMediaThumbnail("large1", 256, client, config);

        Assert.Equal("gramps://media/large1/thumbnail/256", resource.Uri);
        Assert.Equal("image/jpeg", resource.MimeType);
        var info = TestImages.Identify(resource.DecodedData);
        Assert.Equal(256, info.Width);
        Assert.Contains("/api/media/large1/file", handler.RequestPaths);
        Assert.DoesNotContain(handler.RequestPaths, path => path.Contains("/thumbnail/"));
    }

    [Fact]
    public async Task ReadMedia_Thumbnail_Defaults_To_1568_Pixel_Long_Edge()
    {
        var client = CreateClient();
        var config = CreateConfig(mediaResourcesEnabled: true);

        var result = await MediaTools.ReadMedia("large1", client: client, config: config);
        var image = Assert.IsType<ImageContentBlock>(Assert.Single(result.Content));

        Assert.Equal("image/jpeg", image.MimeType);
        var info = TestImages.Identify(image.DecodedData);
        Assert.Equal(MediaPreviewRenderer.DefaultSize, info.Width);
        Assert.InRange(info.Height, 1044, 1046);
    }

    [Fact]
    public async Task ReadMedia_Thumbnail_Does_Not_Upscale_Small_Images()
    {
        var client = CreateClient();
        var config = CreateConfig(mediaResourcesEnabled: true);

        var result = await MediaTools.ReadMedia("handle1", client: client, config: config);
        var image = Assert.IsType<ImageContentBlock>(Assert.Single(result.Content));

        var info = TestImages.Identify(image.DecodedData);
        Assert.Equal((64, 48), (info.Width, info.Height));
    }

    [Fact]
    public async Task ReadMedia_Thumbnail_Converts_Tiff_To_Jpeg()
    {
        var client = CreateClient();
        var config = CreateConfig(mediaResourcesEnabled: true);

        var result = await MediaTools.ReadMedia("tiff1", client: client, config: config);
        var image = Assert.IsType<ImageContentBlock>(Assert.Single(result.Content));

        Assert.Equal("image/jpeg", image.MimeType);
        Assert.Equal(120, TestImages.Identify(image.DecodedData).Width);
    }

    [Fact]
    public async Task ReadMedia_Thumbnail_Result_Serializes_As_Image_Content()
    {
        var client = CreateClient();
        var config = CreateConfig(mediaResourcesEnabled: true);
        var result = await MediaTools.ReadMedia("handle1", size: 256, client: client, config: config);
        var json = JsonSerializer.Serialize(result, McpJsonUtilities.DefaultOptions);
        using var doc = JsonDocument.Parse(json);

        var content = doc.RootElement.GetProperty("content")[0];
        Assert.Equal("image", content.GetProperty("type").GetString());
        Assert.Equal("image/jpeg", content.GetProperty("mimeType").GetString());
        Assert.False(string.IsNullOrWhiteSpace(content.GetProperty("data").GetString()));
    }

    [Theory]
    [InlineData("pdf1", "only available for image media")]
    [InlineData("audio1", "only available for image media")]
    [InlineData("avif1", "cannot be rendered from 'image/avif'")]
    public async Task ReadMedia_Thumbnail_Rejects_Unpreviewable_Mime_Before_Download(string handle, string expected)
    {
        var handler = new MediaHandler();
        var ex = await Assert.ThrowsAsync<McpException>(() => MediaTools.ReadMedia(
            handle, client: CreateClient(handler), config: CreateConfig(mediaResourcesEnabled: true)));

        Assert.Contains(expected, ex.Message);
        Assert.Contains("mode file", ex.Message);
        Assert.DoesNotContain(handler.RequestPaths, path => path.EndsWith("/file", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ReadMedia_Thumbnail_Rejects_Undecodable_File()
    {
        var ex = await Assert.ThrowsAsync<McpException>(() => MediaTools.ReadMedia(
            "garbage1", client: CreateClient(), config: CreateConfig(mediaResourcesEnabled: true)));

        Assert.Contains("not an image the server can render", ex.Message);
    }

    [Fact]
    public async Task ReadMedia_Thumbnail_Previews_Originals_Larger_Than_Media_Max_Bytes()
    {
        const long maxBytes = 100_000;
        Assert.True(NoisyJpeg.Length > maxBytes);
        var config = CreateConfig(mediaResourcesEnabled: true, mediaMaxBytes: maxBytes);

        var result = await MediaTools.ReadMedia("noisy1", size: 64, client: CreateClient(), config: config);
        var image = Assert.IsType<ImageContentBlock>(Assert.Single(result.Content));
        Assert.Equal(64, TestImages.Identify(image.DecodedData).Width);

        var ex = await Assert.ThrowsAsync<McpException>(() => MediaTools.ReadMedia(
            "noisy1", mode: "file", client: CreateClient(), config: config));
        Assert.Contains("limit", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetMediaFile_Returns_Blob_In_ReadOnly_Mode()
    {
        var client = CreateClient(readOnly: true);
        var config = CreateConfig(mediaResourcesEnabled: true, readOnly: true);

        var resource = await GrampsResources.GetMediaFile("handle1", client, config);

        Assert.Equal("gramps://media/handle1/file", resource.Uri);
        Assert.Equal("image/jpeg", resource.MimeType);
        Assert.Equal(SmallJpeg, resource.DecodedData.ToArray());
    }

    [Fact]
    public async Task ReadMedia_File_Returns_ImageContentBlock_For_Image_File()
    {
        var client = CreateClient();
        var config = CreateConfig(mediaResourcesEnabled: true);

        var result = await MediaTools.ReadMedia("handle1", mode: "file", client: client, config: config);

        Assert.Single(result.Content);
        var image = Assert.IsType<ImageContentBlock>(result.Content[0]);
        Assert.Equal("image", image.Type);
        Assert.Equal("image/jpeg", image.MimeType);
        Assert.Equal(SmallJpeg, image.DecodedData.ToArray());
    }

    [Fact]
    public async Task ReadMedia_File_Returns_AudioContentBlock_For_Audio_File()
    {
        var client = CreateClient();
        var config = CreateConfig(mediaResourcesEnabled: true);

        var result = await MediaTools.ReadMedia("audio1", mode: "file", client: client, config: config);

        Assert.Single(result.Content);
        var audio = Assert.IsType<AudioContentBlock>(result.Content[0]);
        Assert.Equal("audio", audio.Type);
        Assert.Equal("audio/aac", audio.MimeType);
        Assert.Equal([19, 20, 21], audio.DecodedData.ToArray());
    }

    [Fact]
    public async Task ReadMedia_File_Returns_EmbeddedResourceBlock_For_Pdf_File()
    {
        var client = CreateClient();
        var config = CreateConfig(mediaResourcesEnabled: true);

        var result = await MediaTools.ReadMedia("pdf1", mode: "file", client: client, config: config);

        Assert.Single(result.Content);
        var embedded = Assert.IsType<EmbeddedResourceBlock>(result.Content[0]);
        Assert.Equal("resource", embedded.Type);
        var blob = Assert.IsType<BlobResourceContents>(embedded.Resource);
        Assert.Equal("gramps://media/pdf1/file", blob.Uri);
        Assert.Equal("application/pdf", blob.MimeType);
        Assert.Equal([16, 17, 18], blob.DecodedData.ToArray());
    }

    [Theory]
    [InlineData("tiff1", "image/tiff", true)]
    [InlineData("avif1", "image/avif", false)]
    public async Task ReadMedia_File_Returns_Blob_And_Hint_For_Images_Clients_Cannot_Display(
        string handle, string mimeType, bool suggestsThumbnail)
    {
        var client = CreateClient();
        var config = CreateConfig(mediaResourcesEnabled: true);

        var result = await MediaTools.ReadMedia(handle, mode: "file", client: client, config: config);

        Assert.Equal(2, result.Content.Count);
        var blob = Assert.IsType<BlobResourceContents>(Assert.IsType<EmbeddedResourceBlock>(result.Content[0]).Resource);
        Assert.Equal(mimeType, blob.MimeType);
        var hint = Assert.IsType<TextContentBlock>(result.Content[1]);
        Assert.Contains($"'{mimeType}' is returned as an embedded resource", hint.Text);
        Assert.Equal(suggestsThumbnail, hint.Text.Contains("mode thumbnail"));
    }

    [Fact]
    public async Task GetMediaFile_Returns_Blob_For_Audio()
    {
        var client = CreateClient();
        var config = CreateConfig(mediaResourcesEnabled: true);

        var resource = await GrampsResources.GetMediaFile("audio1", client, config);

        Assert.Equal("gramps://media/audio1/file", resource.Uri);
        Assert.Equal("audio/aac", resource.MimeType);
        Assert.Equal([19, 20, 21], resource.DecodedData.ToArray());
    }

    [Fact]
    public async Task ReadMedia_File_Result_Serializes_As_Typed_Content()
    {
        var client = CreateClient();
        var config = CreateConfig(mediaResourcesEnabled: true);
        var result = await MediaTools.ReadMedia("handle1", mode: "file", client: client, config: config);

        var json = JsonSerializer.Serialize(result, McpJsonUtilities.DefaultOptions);
        using var doc = JsonDocument.Parse(json);

        var content = doc.RootElement.GetProperty("content")[0];
        Assert.Equal("image", content.GetProperty("type").GetString());
        Assert.Equal("image/jpeg", content.GetProperty("mimeType").GetString());
        Assert.False(string.IsNullOrWhiteSpace(content.GetProperty("data").GetString()));
    }

    [Fact]
    public async Task GetMediaFile_Fails_When_Media_Resources_Disabled()
    {
        var handler = new MediaHandler();
        var client = CreateClient(handler);
        var config = CreateConfig(mediaResourcesEnabled: false);

        var ex = await Assert.ThrowsAsync<McpException>(
            () => GrampsResources.GetMediaFile("handle1", client, config));

        Assert.Contains("Media file resources are disabled", ex.Message);
        Assert.Empty(handler.RequestPaths);
    }

    [Fact]
    public async Task ReadMedia_Thumbnail_Fails_When_Media_Resources_Disabled_Before_Http()
    {
        var handler = new MediaHandler();
        var client = CreateClient(handler);
        var config = CreateConfig(mediaResourcesEnabled: false);

        var ex = await Assert.ThrowsAsync<McpException>(
            () => MediaTools.ReadMedia("handle1", size: 256, client: client, config: config));

        Assert.Contains("Media file resources are disabled", ex.Message);
        Assert.Empty(handler.RequestPaths);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GetMediaFile_Fails_When_Handle_Is_Empty(string handle)
    {
        var client = CreateClient();
        var config = CreateConfig(mediaResourcesEnabled: true);

        var ex = await Assert.ThrowsAsync<McpException>(
            () => GrampsResources.GetMediaFile(handle, client, config));

        Assert.Contains("Media handle must not be empty", ex.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ReadMedia_File_Fails_When_Handle_Is_Empty(string handle)
    {
        var handler = new MediaHandler();
        var client = CreateClient(handler);
        var config = CreateConfig(mediaResourcesEnabled: true);

        var ex = await Assert.ThrowsAsync<McpException>(
            () => MediaTools.ReadMedia(handle, mode: "file", client: client, config: config));

        Assert.Contains("Media handle must not be empty", ex.Message);
        Assert.Empty(handler.RequestPaths);
    }

    [Fact]
    public async Task GetMediaFile_Fails_For_Private_Media_By_Default()
    {
        var client = CreateClient();
        var config = CreateConfig(mediaResourcesEnabled: true);

        var ex = await Assert.ThrowsAsync<McpException>(
            () => GrampsResources.GetMediaFile("private1", client, config));

        Assert.Contains("private media records", ex.Message);
    }

    [Fact]
    public async Task GetMediaFile_Allows_Private_Media_When_Configured()
    {
        var client = CreateClient();
        var config = CreateConfig(mediaResourcesEnabled: true, mediaAllowPrivate: true);

        var resource = await GrampsResources.GetMediaFile("private1", client, config);

        Assert.Equal(SmallJpeg, resource.DecodedData.ToArray());
    }

    [Fact]
    public async Task GetMediaThumbnail_Allows_Private_Media_When_Configured()
    {
        var client = CreateClient();
        var config = CreateConfig(mediaResourcesEnabled: true, mediaAllowPrivate: true);

        var resource = await GrampsResources.GetMediaThumbnail("private1", 32, client, config);

        Assert.Equal(32, TestImages.Identify(resource.DecodedData).Width);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(4097)]
    public async Task GetMediaThumbnail_Fails_When_Size_Is_Out_Of_Range(int size)
    {
        var handler = new MediaHandler();
        var client = CreateClient(handler);
        var config = CreateConfig(mediaResourcesEnabled: true);

        var ex = await Assert.ThrowsAsync<McpException>(
            () => GrampsResources.GetMediaThumbnail("handle1", size, client, config));

        Assert.Contains("Thumbnail size must be an integer from 1 to 4096 pixels", ex.Message);
        Assert.Empty(handler.RequestPaths);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(4097)]
    public async Task ReadMedia_Thumbnail_Fails_When_Size_Is_Out_Of_Range(int size)
    {
        var client = CreateClient();
        var config = CreateConfig(mediaResourcesEnabled: true);

        var ex = await Assert.ThrowsAsync<McpException>(
            () => MediaTools.ReadMedia("handle1", size: size, client: client, config: config));

        Assert.Contains("Thumbnail size must be an integer from 1 to 4096 pixels", ex.Message);
    }

    [Fact]
    public async Task GetMediaFile_Fails_When_Metadata_Is_Missing()
    {
        var client = CreateClient();
        var config = CreateConfig(mediaResourcesEnabled: true);

        var ex = await Assert.ThrowsAsync<McpException>(
            () => GrampsResources.GetMediaFile("missing1", client, config));

        Assert.Contains("Media not found", ex.Message);
    }

    [Theory]
    [InlineData("bad", null)]
    [InlineData("", null)]
    [InlineData("file", 256)]
    [InlineData("thumbnail", 0)]
    [InlineData("thumbnail", -1)]
    [InlineData("thumbnail", 4097)]
    public async Task ReadMedia_InvalidOptionsFailBeforeIdResolution(string mode, int? size)
    {
        var handler = new MediaHandler();
        await Assert.ThrowsAsync<McpException>(() => MediaTools.ReadMedia(
            "O1234567", mode, size, CreateClient(handler), CreateConfig(mediaResourcesEnabled: true)));
        Assert.Empty(handler.RequestPaths);
    }

    [Theory]
    [InlineData("thumbnail")]
    [InlineData("file")]
    public async Task ReadMedia_DisabledFailsBeforeIdResolution(string mode)
    {
        var handler = new MediaHandler();
        var ex = await Assert.ThrowsAsync<McpException>(() => MediaTools.ReadMedia(
            "O1234567", mode, client: CreateClient(handler), config: CreateConfig(mediaResourcesEnabled: false)));
        Assert.Contains("disabled", ex.Message);
        Assert.Empty(handler.RequestPaths);
    }

    [Theory]
    [InlineData("thumbnail")]
    [InlineData("file")]
    public async Task ReadMedia_PrivateRecordsRemainBlocked(string mode)
    {
        var handler = new MediaHandler();
        var ex = await Assert.ThrowsAsync<McpException>(() => MediaTools.ReadMedia(
            "private1", mode, client: CreateClient(handler), config: CreateConfig(mediaResourcesEnabled: true)));
        Assert.Contains("private media records", ex.Message);
        Assert.DoesNotContain(handler.RequestPaths, path => path.Contains("/file") || path.Contains("/thumbnail/"));
    }

    [Theory]
    [InlineData("thumbnail")]
    [InlineData("file")]
    public async Task ReadMedia_RespectsByteLimit(string mode)
    {
        var ex = await Assert.ThrowsAsync<McpException>(() => MediaTools.ReadMedia(
            "handle1", mode, client: CreateClient(), config: CreateConfig(mediaResourcesEnabled: true, mediaMaxBytes: 2)));
        Assert.Contains("limit", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReadMedia_ResolvesGrampsIdAndRendersCustomThumbnailSizeInReadOnlyMode()
    {
        HandleCache.Invalidate();
        var handler = new MediaHandler();
        var result = await MediaTools.ReadMedia("O7654321", " Thumbnail ", 512,
            CreateClient(handler, readOnly: true), CreateConfig(mediaResourcesEnabled: true, readOnly: true));
        var image = Assert.IsType<ImageContentBlock>(Assert.Single(result.Content));
        Assert.Equal(512, TestImages.Identify(image.DecodedData).Width);
        Assert.Contains("/api/media/", handler.RequestPaths);
        Assert.Contains("/api/media/large1/file", handler.RequestPaths);
        Assert.DoesNotContain(handler.RequestPaths, path => path.Contains("/thumbnail/"));
    }

    private static GrampsApiClient CreateClient(bool readOnly = false)
    {
        return CreateClient(new MediaHandler(), readOnly);
    }

    private static GrampsApiClient CreateClient(MediaHandler handler, bool readOnly = false)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://gramps-web.test")
        };
        var config = CreateConfig(mediaResourcesEnabled: true, readOnly: readOnly);
        var tokenProvider = new GrampsAuthTokenProvider(
            new HttpClient(handler),
            config,
            NullLogger<GrampsAuthTokenProvider>.Instance);
        return new GrampsApiClient(
            httpClient,
            config,
            NullLogger<GrampsApiClient>.Instance,
            tokenProvider);
    }

    private static GrampsConfig CreateConfig(
        bool mediaResourcesEnabled,
        bool readOnly = false,
        long mediaMaxBytes = GrampsConfig.DefaultMediaMaxBytes,
        bool mediaAllowPrivate = false)
    {
        return new GrampsConfig(
            ApiUrl: "https://gramps-web.test",
            Username: "user",
            Password: "pass",
            TreeId: "tree",
            ReadOnly: readOnly,
            MediaResourcesEnabled: mediaResourcesEnabled,
            MediaMaxBytes: mediaMaxBytes,
            MediaAllowPrivate: mediaAllowPrivate);
    }

    private sealed class MediaHandler : HttpMessageHandler
    {
        public List<string> RequestPaths { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            RequestPaths.Add(path);

            if (request.Method == HttpMethod.Post && path == "/api/token/")
                return Task.FromResult(JsonResponse(TokenJson));

            if (request.Method == HttpMethod.Get && path.StartsWith("/api/media/", StringComparison.Ordinal))
                return Task.FromResult(HandleMediaRequest(path));

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("not found", Encoding.UTF8, "text/plain")
            });
        }

        private static HttpResponseMessage HandleMediaRequest(string path)
        {
            return path switch
            {
                "/api/media/" => JsonResponse("""[{"handle":"large1","gramps_id":"O7654321"}]"""),
                "/api/media/handle1" => JsonResponse(MediaJson("handle1", "image/jpeg", isPrivate: false)),
                "/api/media/handle1/file" => BinaryResponse(SmallJpeg, "image/jpeg"),
                "/api/media/large1" => JsonResponse(MediaJson("large1", "image/jpeg", isPrivate: false)),
                "/api/media/large1/file" => BinaryResponse(LargeJpeg, "image/jpeg"),
                "/api/media/noisy1" => JsonResponse(MediaJson("noisy1", "image/jpeg", isPrivate: false)),
                "/api/media/noisy1/file" => BinaryResponse(NoisyJpeg, "image/jpeg"),
                "/api/media/private1" => JsonResponse(MediaJson("private1", "image/jpeg", isPrivate: true)),
                "/api/media/private1/file" => BinaryResponse(SmallJpeg, "image/jpeg"),
                "/api/media/tiff1" => JsonResponse(MediaJson("tiff1", "image/tiff", isPrivate: false)),
                "/api/media/tiff1/file" => BinaryResponse(SmallTiff, "image/tiff"),
                "/api/media/avif1" => JsonResponse(MediaJson("avif1", "image/avif", isPrivate: false)),
                "/api/media/avif1/file" => BinaryResponse([13, 14, 15], "image/avif"),
                "/api/media/garbage1" => JsonResponse(MediaJson("garbage1", "image/jpeg", isPrivate: false)),
                "/api/media/garbage1/file" => BinaryResponse([1, 2, 3], "image/jpeg"),
                "/api/media/pdf1" => JsonResponse(MediaJson("pdf1", "application/pdf", isPrivate: false)),
                "/api/media/pdf1/file" => BinaryResponse([16, 17, 18], "application/pdf"),
                "/api/media/audio1" => JsonResponse(MediaJson("audio1", "audio/aac", isPrivate: false)),
                "/api/media/audio1/file" => BinaryResponse([19, 20, 21], "audio/aac"),
                "/api/media/missing1" => new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new StringContent("not found", Encoding.UTF8, "text/plain")
                },
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new StringContent("not found", Encoding.UTF8, "text/plain")
                }
            };
        }

        private const string TokenJson = """
            {
              "access_token": "token",
              "refresh_token": "refresh",
              "expires_in": 900
            }
            """;

        private static string MediaJson(string handle, string mimeType, bool isPrivate)
        {
            return $$"""
                {
                  "handle": "{{handle}}",
                  "gramps_id": "M1",
                  "path": "media.jpg",
                  "mime": "{{mimeType}}",
                  "desc": "Media",
                  "private": {{isPrivate.ToString().ToLowerInvariant()}}
                }
                """;
        }

        private static HttpResponseMessage JsonResponse(string json)
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        }

        private static HttpResponseMessage BinaryResponse(byte[] bytes, string mimeType)
        {
            var content = new ByteArrayContent(bytes);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(mimeType);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = content
            };
        }
    }
}
