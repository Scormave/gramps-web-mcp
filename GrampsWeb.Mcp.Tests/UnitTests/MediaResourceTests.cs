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

public class MediaResourceTests
{
    [Fact]
    public async Task GetMediaThumbnail_Returns_Blob_When_Enabled()
    {
        var client = CreateClient();
        var config = CreateConfig(mediaResourcesEnabled: true);

        var resource = await GrampsResources.GetMediaThumbnail("handle1", 256, client, config);

        Assert.Equal("gramps://media/handle1/thumbnail/256", resource.Uri);
        Assert.Equal("image/jpeg", resource.MimeType);
        Assert.Equal([1, 2, 3], resource.DecodedData.ToArray());
    }

    [Fact]
    public async Task GetMediaThumbnail_Tool_Returns_ImageContentBlock_When_Enabled()
    {
        var client = CreateClient();
        var config = CreateConfig(mediaResourcesEnabled: true);

        var image = await MediaTools.GetMediaThumbnail("handle1", 256, client, config);

        Assert.Equal("image", image.Type);
        Assert.Equal("image/jpeg", image.MimeType);
        Assert.Equal([1, 2, 3], image.DecodedData.ToArray());
    }

    [Fact]
    public async Task GetMediaThumbnail_Tool_Result_Serializes_As_Image_Content()
    {
        var client = CreateClient();
        var config = CreateConfig(mediaResourcesEnabled: true);
        var image = await MediaTools.GetMediaThumbnail("handle1", 256, client, config);

        var result = new CallToolResult { Content = [image] };
        var json = JsonSerializer.Serialize(result, McpJsonUtilities.DefaultOptions);
        using var doc = JsonDocument.Parse(json);

        var content = doc.RootElement.GetProperty("content")[0];
        Assert.Equal("image", content.GetProperty("type").GetString());
        Assert.Equal("image/jpeg", content.GetProperty("mimeType").GetString());
        Assert.False(string.IsNullOrWhiteSpace(content.GetProperty("data").GetString()));
    }

    [Fact]
    public async Task GetMediaFile_Returns_Blob_In_ReadOnly_Mode()
    {
        var client = CreateClient(readOnly: true);
        var config = CreateConfig(mediaResourcesEnabled: true, readOnly: true);

        var resource = await GrampsResources.GetMediaFile("handle1", client, config);

        Assert.Equal("gramps://media/handle1/file", resource.Uri);
        Assert.Equal("image/jpeg", resource.MimeType);
        Assert.Equal([4, 5, 6], resource.DecodedData.ToArray());
    }

    [Fact]
    public async Task GetMediaFile_Tool_Returns_ImageContentBlock_For_Image_File()
    {
        var client = CreateClient();
        var config = CreateConfig(mediaResourcesEnabled: true);

        var result = await MediaTools.GetMediaFile("handle1", client, config);

        Assert.Single(result.Content);
        var image = Assert.IsType<ImageContentBlock>(result.Content[0]);
        Assert.Equal("image", image.Type);
        Assert.Equal("image/jpeg", image.MimeType);
        Assert.Equal([4, 5, 6], image.DecodedData.ToArray());
    }

    [Fact]
    public async Task GetMediaFile_Tool_Returns_AudioContentBlock_For_Audio_File()
    {
        var client = CreateClient();
        var config = CreateConfig(
            mediaResourcesEnabled: true,
            mediaAllowedMimeTypes: ["image/jpeg", "audio/aac"]);

        var result = await MediaTools.GetMediaFile("audio1", client, config);

        Assert.Single(result.Content);
        var audio = Assert.IsType<AudioContentBlock>(result.Content[0]);
        Assert.Equal("audio", audio.Type);
        Assert.Equal("audio/aac", audio.MimeType);
        Assert.Equal([19, 20, 21], audio.DecodedData.ToArray());
    }

    [Fact]
    public async Task GetMediaFile_Tool_Returns_EmbeddedResourceBlock_For_Pdf_File()
    {
        var client = CreateClient();
        var config = CreateConfig(
            mediaResourcesEnabled: true,
            mediaAllowedMimeTypes: ["image/jpeg", "application/pdf"]);

        var result = await MediaTools.GetMediaFile("pdf1", client, config);

        Assert.Single(result.Content);
        var embedded = Assert.IsType<EmbeddedResourceBlock>(result.Content[0]);
        Assert.Equal("resource", embedded.Type);
        var blob = Assert.IsType<BlobResourceContents>(embedded.Resource);
        Assert.Equal("gramps://media/pdf1/file", blob.Uri);
        Assert.Equal("application/pdf", blob.MimeType);
        Assert.Equal([16, 17, 18], blob.DecodedData.ToArray());
    }

    [Fact]
    public async Task GetMediaFile_Returns_Blob_For_Audio_When_Allowlisted()
    {
        var client = CreateClient();
        var config = CreateConfig(
            mediaResourcesEnabled: true,
            mediaAllowedMimeTypes: ["audio/aac"]);

        var resource = await GrampsResources.GetMediaFile("audio1", client, config);

        Assert.Equal("gramps://media/audio1/file", resource.Uri);
        Assert.Equal("audio/aac", resource.MimeType);
        Assert.Equal([19, 20, 21], resource.DecodedData.ToArray());
    }

    [Fact]
    public async Task GetMediaFile_Tool_Result_Serializes_As_Typed_Content()
    {
        var client = CreateClient();
        var config = CreateConfig(mediaResourcesEnabled: true);
        var result = await MediaTools.GetMediaFile("handle1", client, config);

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
    public async Task GetMediaThumbnail_Tool_Fails_When_Media_Resources_Disabled_Before_Http()
    {
        var handler = new MediaHandler();
        var client = CreateClient(handler);
        var config = CreateConfig(mediaResourcesEnabled: false);

        var ex = await Assert.ThrowsAsync<McpException>(
            () => MediaTools.GetMediaThumbnail("handle1", 256, client, config));

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
    public async Task GetMediaFile_Tool_Fails_When_Handle_Is_Empty(string handle)
    {
        var handler = new MediaHandler();
        var client = CreateClient(handler);
        var config = CreateConfig(mediaResourcesEnabled: true);

        var ex = await Assert.ThrowsAsync<McpException>(
            () => MediaTools.GetMediaFile(handle, client, config));

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

        Assert.Equal([7, 8, 9], resource.DecodedData.ToArray());
    }

    [Fact]
    public async Task GetMediaFile_Fails_When_Metadata_Mime_Is_Not_Allowed()
    {
        var client = CreateClient();
        var config = CreateConfig(
            mediaResourcesEnabled: true,
            mediaAllowedMimeTypes: ["image/jpeg"]);

        var ex = await Assert.ThrowsAsync<McpException>(
            () => GrampsResources.GetMediaFile("tiff1", client, config));

        Assert.Contains("not allowed", ex.Message);
    }

    [Fact]
    public async Task GetMediaThumbnail_Fails_When_Response_Mime_Is_Not_Allowed()
    {
        var client = CreateClient();
        var config = CreateConfig(
            mediaResourcesEnabled: true,
            mediaAllowedMimeTypes: ["image/jpeg"]);

        var ex = await Assert.ThrowsAsync<McpException>(
            () => GrampsResources.GetMediaThumbnail("png1", 256, client, config));

        Assert.Contains("not allowed", ex.Message);
    }

    [Fact]
    public async Task GetMediaFile_Fails_When_Response_Mime_Is_Not_Allowed()
    {
        var client = CreateClient();
        var config = CreateConfig(
            mediaResourcesEnabled: true,
            mediaAllowedMimeTypes: ["image/jpeg"]);

        var ex = await Assert.ThrowsAsync<McpException>(
            () => GrampsResources.GetMediaFile("mismatch1", client, config));

        Assert.Contains("not allowed", ex.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task GetMediaThumbnail_Fails_When_Size_Is_Not_Positive(int size)
    {
        var client = CreateClient();
        var config = CreateConfig(mediaResourcesEnabled: true);

        var ex = await Assert.ThrowsAsync<McpException>(
            () => GrampsResources.GetMediaThumbnail("handle1", size, client, config));

        Assert.Contains("Thumbnail size must be a positive integer", ex.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task GetMediaThumbnail_Tool_Fails_When_Size_Is_Not_Positive(int size)
    {
        var client = CreateClient();
        var config = CreateConfig(mediaResourcesEnabled: true);

        var ex = await Assert.ThrowsAsync<McpException>(
            () => MediaTools.GetMediaThumbnail("handle1", size, client, config));

        Assert.Contains("Thumbnail size must be a positive integer", ex.Message);
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
        long mediaMaxBytes = 1024,
        string[]? mediaAllowedMimeTypes = null,
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
            MediaAllowedMimeTypes: mediaAllowedMimeTypes ?? ["image/jpeg", "image/png"],
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
                "/api/media/handle1" => JsonResponse(MediaJson("handle1", "image/jpeg", isPrivate: false)),
                "/api/media/handle1/file" => BinaryResponse([4, 5, 6], "image/jpeg"),
                "/api/media/handle1/thumbnail/256" => BinaryResponse([1, 2, 3], "image/jpeg"),
                "/api/media/private1" => JsonResponse(MediaJson("private1", "image/jpeg", isPrivate: true)),
                "/api/media/private1/file" => BinaryResponse([7, 8, 9], "image/jpeg"),
                "/api/media/tiff1" => JsonResponse(MediaJson("tiff1", "image/tiff", isPrivate: false)),
                "/api/media/png1" => JsonResponse(MediaJson("png1", "image/png", isPrivate: false)),
                "/api/media/png1/thumbnail/256" => BinaryResponse([10, 11, 12], "image/png"),
                "/api/media/mismatch1" => JsonResponse(MediaJson("mismatch1", "image/jpeg", isPrivate: false)),
                "/api/media/mismatch1/file" => BinaryResponse([13, 14, 15], "image/tiff"),
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
