using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Config;
using GrampsWeb.Mcp.Input;
using GrampsWeb.Mcp.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

/// <summary>
/// create_media and update_media's filePath against a fake Gramps Web: what is uploaded, in which order,
/// and that a stored copy of the file is reused instead of uploaded again.
/// </summary>
public class MediaUploadToolsTests : IDisposable
{
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 1, 2, 3];
    private static readonly string JpegChecksum = MediaUploadFile.ChecksumOf(Jpeg);

    private readonly string _uploads = Directory.CreateDirectory(
        Path.Combine(Path.GetTempPath(), "gramps-upload-tools-" + Guid.NewGuid().ToString("N"))).FullName;

    public MediaUploadToolsTests() => File.WriteAllBytes(Path.Combine(_uploads, "metric book 1890.jpg"), Jpeg);

    public void Dispose() => Directory.Delete(_uploads, recursive: true);

    [Fact]
    public async Task CreateMedia_Uploads_The_Bytes_Then_Saves_The_Record_And_Shows_It_On_The_Owner()
    {
        var server = new FakeGramps();
        server.Lists["/api/people/?gramps_id=I0001&pagesize=1"] = """[{"handle":"h-person","gramps_id":"I0001"}]""";
        server.Objects["/api/people/h-person"] = """{"_class":"Person","handle":"h-person","gramps_id":"I0001","media_list":[{"_class":"MediaRef","ref":"h-photo"}]}""";

        var result = await MediaTools.CreateMedia("metric book 1890.jpg", date: "1890",
            attachTo: Links("I0001"), noteHandles: Handles("h-note"), client: server.Client(), config: Config());

        Assert.Equal(
        [
            "GET /api/people/?gramps_id=I0001&pagesize=1",
            "GET /api/people/h-person",
            $"GET /api/media/?gql={Uri.EscapeDataString($"checksum = \"{JpegChecksum}\"")}&page=1&pagesize=1",
            "POST /api/media/",
            "GET /api/media/h-new",
            "PUT /api/media/h-new",
            "GET /api/people/h-person",
            "PUT /api/people/h-person",
        ], server.Requests);
        Assert.Equal("image/jpeg", server.UploadContentType);
        Assert.Equal(Jpeg, server.UploadBytes);

        var media = server.Saved["/api/media/h-new"];
        Assert.Equal("metric book 1890", media["desc"]!.GetValue<string>());
        Assert.Equal(JpegChecksum, media["checksum"]!.GetValue<string>());
        Assert.Equal(1890, media["date"]!["dateval"]![2]!.GetValue<int>());
        Assert.Equal(["h-note"], media["note_list"]!.AsArray().Select(n => n!.GetValue<string>()));
        Assert.Null(media["private"]);
        Assert.Equal(["h-photo", "h-new"],
            server.Saved["/api/people/h-person"]["media_list"]!.AsArray().Select(r => r!["ref"]!.GetValue<string>()));

        Assert.Contains("action: created", result);
        Assert.Contains("gramps_id: O0042", result);
        Assert.Contains("""attached_to: ["I0001"]""", result);
        Assert.DoesNotContain("Next steps", result);
    }

    [Fact]
    public async Task CreateMedia_Reuses_A_Stored_Identical_File_And_Only_Adds_Missing_Links()
    {
        var server = new FakeGramps { StoredByChecksum = """[{"handle":"h-old","gramps_id":"O0007","checksum":"x","mime":"image/jpeg","desc":"Old scan"}]""" };
        server.Objects["/api/events/h-event-handle-1"] = """{"_class":"Event","handle":"h-event-handle-1","gramps_id":"E0003","media_list":[]}""";
        server.Objects["/api/people/h-person-handle-1"] =
            """{"_class":"Person","handle":"h-person-handle-1","gramps_id":"I0002","media_list":[{"_class":"MediaRef","ref":"h-old"}]}""";

        var result = await MediaTools.CreateMedia("metric book 1890.jpg", description: "ignored",
            attachTo: Links("h-event-handle-1", "h-person-handle-1"), client: server.Client(), config: Config());

        Assert.DoesNotContain("POST /api/media/", server.Requests);
        Assert.DoesNotContain("PUT /api/media/h-old", server.Requests);
        Assert.DoesNotContain("PUT /api/people/h-person-handle-1", server.Requests);
        Assert.Equal(["h-old"], server.Saved["/api/events/h-event-handle-1"]["media_list"]!.AsArray().Select(r => r!["ref"]!.GetValue<string>()));
        Assert.Contains("action: existing", result);
        Assert.Contains("gramps_id: O0007", result);
        Assert.Contains("""attached_to: ["E0003","I0002"]""", result);
        Assert.Contains("nothing was uploaded", result);
    }

    [Theory]
    [InlineData("I0009", "there is no person I0009")]
    [InlineData("N0001", "media can be shown only on those")]
    [InlineData("h-nowhere-handle-1", "no person, family, event, place, source or citation has handle")]
    [InlineData("Ivan", "neither a Gramps ID")]
    public async Task CreateMedia_Rejects_Unknown_Owners_Before_Uploading(string owner, string message)
    {
        var server = new FakeGramps();
        server.Lists["/api/people/?gramps_id=I0009&pagesize=1"] = "[]";

        var ex = await Assert.ThrowsAsync<McpException>(() => MediaTools.CreateMedia("metric book 1890.jpg",
            attachTo: Links(owner), client: server.Client(), config: Config()));

        Assert.Contains(message, ex.Message);
        Assert.DoesNotContain(server.Requests, r => r.StartsWith("POST", StringComparison.Ordinal) || r.StartsWith("PUT", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CreateMedia_Rejects_An_Unreadable_Date_Before_Uploading()
    {
        var server = new FakeGramps();

        var ex = await Assert.ThrowsAsync<McpException>(() => MediaTools.CreateMedia("metric book 1890.jpg",
            date: "1890-02-30", client: server.Client(), config: Config()));

        Assert.Contains("1890-02-30", ex.Message);
        Assert.DoesNotContain("POST /api/media/", server.Requests);
    }

    [Fact]
    public async Task CreateMedia_Without_Owners_Suggests_How_To_Link()
    {
        var server = new FakeGramps();

        var result = await MediaTools.CreateMedia("metric book 1890.jpg", isPrivate: true, client: server.Client(), config: Config());

        Assert.True(server.Saved["/api/media/h-new"]["private"]!.GetValue<bool>());
        Assert.Contains("Next steps", result);
        Assert.Contains("attached_to: none", result);
    }

    [Fact]
    public async Task CreateMedia_Timeout_Says_A_Retry_Is_Safe()
    {
        var server = new FakeGramps { UploadTimesOut = true };

        var ex = await Assert.ThrowsAsync<McpException>(() => MediaTools.CreateMedia("metric book 1890.jpg", client: server.Client(), config: Config()));

        Assert.Contains("did not finish in time", ex.Message);
        Assert.Contains("found by checksum", ex.Message);
    }

    [Fact]
    public async Task UpdateMedia_Replaces_The_File_Before_Loading_The_Record_It_Saves()
    {
        var server = new FakeGramps();
        server.Objects["/api/media/h-media"] = """{"_class":"Media","handle":"h-media","gramps_id":"O0100","checksum":"0ld","mime":"image/png","desc":"old"}""";

        var result = await MediaTools.UpdateMedia("h-media", description: "Page 2", filePath: "metric book 1890.jpg",
            client: server.Client(), config: Config());

        Assert.Equal(
        [
            "GET /api/media/h-media",
            $"GET /api/media/?gql={Uri.EscapeDataString($"checksum = \"{JpegChecksum}\"")}&page=1&pagesize=1",
            "PUT /api/media/h-media/file",
            "GET /api/media/h-media",
            "PUT /api/media/h-media",
        ], server.Requests);
        Assert.Equal(Jpeg, server.UploadBytes);
        Assert.Equal(JpegChecksum, server.Saved["/api/media/h-media"]["checksum"]!.GetValue<string>());
        Assert.Equal("image/jpeg", server.Saved["/api/media/h-media"]["mime"]!.GetValue<string>());
        Assert.Contains("file: replaced", result);
    }

    [Fact]
    public async Task UpdateMedia_File_Only_Saves_No_Metadata()
    {
        var server = new FakeGramps();
        server.Objects["/api/media/h-media"] = """{"_class":"Media","handle":"h-media","gramps_id":"O0100","checksum":"0ld","mime":"image/png","desc":"old"}""";

        var result = await MediaTools.UpdateMedia("h-media", filePath: "metric book 1890.jpg", client: server.Client(), config: Config());

        Assert.Equal("PUT /api/media/h-media/file", server.Requests[^1]);
        Assert.Contains("action: updated", result);
        Assert.Contains("file: replaced", result);
    }

    [Fact]
    public async Task UpdateMedia_Same_File_Uploads_Nothing()
    {
        var server = new FakeGramps();
        server.Objects["/api/media/h-media"] = $$"""{"_class":"Media","handle":"h-media","gramps_id":"O0100","checksum":"{{JpegChecksum}}","mime":"image/jpeg"}""";

        var result = await MediaTools.UpdateMedia("h-media", filePath: "metric book 1890.jpg", client: server.Client(), config: Config());

        Assert.Equal(["GET /api/media/h-media"], server.Requests);
        Assert.Contains("file: unchanged (identical)", result);
    }

    [Fact]
    public async Task UpdateMedia_Refuses_A_File_Another_Record_Holds()
    {
        var server = new FakeGramps { StoredByChecksum = """[{"handle":"h-other","gramps_id":"O0007"}]""" };
        server.Objects["/api/media/h-media"] = """{"_class":"Media","handle":"h-media","gramps_id":"O0100","checksum":"0ld"}""";

        var ex = await Assert.ThrowsAsync<McpException>(() => MediaTools.UpdateMedia("h-media", description: "x",
            filePath: "metric book 1890.jpg", client: server.Client(), config: Config()));

        Assert.Contains("already stored as media O0007", ex.Message);
        Assert.DoesNotContain(server.Requests, r => r.StartsWith("PUT", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CreateMedia_Shows_A_Region_On_One_Owner_And_The_Whole_Image_On_Another()
    {
        var server = new FakeGramps();
        server.Lists["/api/people/?gramps_id=I0001&pagesize=1"] = """[{"handle":"h-person","gramps_id":"I0001"}]""";
        server.Objects["/api/people/h-person"] = """{"_class":"Person","handle":"h-person","gramps_id":"I0001","media_list":[{"_class":"MediaRef","ref":"h-photo"}]}""";
        server.Lists["/api/citations/?gramps_id=C0001&pagesize=1"] = """[{"handle":"h-citation","gramps_id":"C0001"}]""";
        server.Objects["/api/citations/h-citation"] = """{"_class":"Citation","handle":"h-citation","gramps_id":"C0001"}""";

        var result = await MediaTools.CreateMedia("metric book 1890.jpg",
            attachTo: LinksJson("""[{"ref":"I0001","region":[10,20,60,45]},"C0001"]"""), client: server.Client(), config: Config());

        Assert.Equal(
            """[{"_class":"MediaRef","ref":"h-photo"},{"_class":"MediaRef","ref":"h-new","rect":[10,20,60,45]}]""",
            server.Saved["/api/people/h-person"]["media_list"]!.ToJsonString());
        Assert.Equal("""[{"_class":"MediaRef","ref":"h-new"}]""", server.Saved["/api/citations/h-citation"]["media_list"]!.ToJsonString());
        Assert.Contains("""attached_to: ["I0001 region [10, 20, 60, 45]","C0001"]""", result);
    }

    [Fact]
    public async Task CreateMedia_Rejects_An_Unreadable_Region_Before_Uploading()
    {
        var server = new FakeGramps();

        var ex = await Assert.ThrowsAsync<McpException>(() => MediaTools.CreateMedia("metric book 1890.jpg",
            attachTo: LinksJson("""[{"ref":"I0001","region":[10,20,5,45]}]"""), client: server.Client(), config: Config()));

        Assert.Contains("attachTo: I0001: region [10, 20, 5, 45] is empty", ex.Message);
        Assert.Empty(server.Requests);
    }

    [Fact]
    public async Task UpdateMedia_AttachTo_Gives_A_Shown_Scan_Its_Region_Once()
    {
        var server = new FakeGramps();
        server.Objects["/api/media/h-media"] = """{"_class":"Media","handle":"h-media","gramps_id":"O0100","checksum":"0ld","desc":"Page 12"}""";
        server.Lists["/api/people/?gramps_id=I0002&pagesize=1"] = """[{"handle":"h-person","gramps_id":"I0002"}]""";
        server.Objects["/api/people/h-person"] =
            """{"_class":"Person","handle":"h-person","gramps_id":"I0002","media_list":[{"_class":"MediaRef","ref":"h-media","private":true}]}""";
        var attachTo = LinksJson("""[{"ref":"I0002","region":"5,50,95,75"}]""");

        var result = await MediaTools.UpdateMedia("h-media", client: server.Client(), config: Config(), attachTo: attachTo);

        Assert.DoesNotContain("PUT /api/media/h-media", server.Requests);
        Assert.Equal("""[{"_class":"MediaRef","ref":"h-media","private":true,"rect":[5,50,95,75]}]""",
            server.Saved["/api/people/h-person"]["media_list"]!.ToJsonString());
        Assert.Contains("action: updated", result);
        Assert.Contains("gramps_id: O0100", result);
        Assert.Contains("""attached_to: ["I0002 region [5, 50, 95, 75]"]""", result);

        server.Requests.Clear();
        await MediaTools.UpdateMedia("h-media", client: server.Client(), config: Config(), attachTo: attachTo);

        Assert.DoesNotContain(server.Requests, r => r.StartsWith("PUT", StringComparison.Ordinal));
    }

    [Fact]
    public async Task UpdateMedia_AttachTo_Saves_Metadata_Then_Links()
    {
        var server = new FakeGramps();
        server.Objects["/api/media/h-media"] = """{"_class":"Media","handle":"h-media","gramps_id":"O0100","checksum":"0ld","desc":"old"}""";
        server.Objects["/api/events/h-event-handle-1"] = """{"_class":"Event","handle":"h-event-handle-1","gramps_id":"E0003","media_list":[]}""";

        var result = await MediaTools.UpdateMedia("h-media", description: "Page 12", client: server.Client(), config: Config(),
            attachTo: Links("h-event-handle-1"));

        Assert.Equal(["PUT /api/media/h-media", "PUT /api/events/h-event-handle-1"],
            server.Requests.Where(r => r.StartsWith("PUT", StringComparison.Ordinal)));
        Assert.Equal("Page 12", server.Saved["/api/media/h-media"]["desc"]!.GetValue<string>());
        Assert.Contains("""attached_to: ["E0003"]""", result);
    }

    [Fact]
    public async Task UpdateMedia_Rejects_An_Unknown_Owner_Before_Changing_Anything()
    {
        var server = new FakeGramps();
        server.Objects["/api/media/h-media"] = """{"_class":"Media","handle":"h-media","gramps_id":"O0100","checksum":"0ld"}""";
        server.Lists["/api/people/?gramps_id=I0009&pagesize=1"] = "[]";

        var ex = await Assert.ThrowsAsync<McpException>(() => MediaTools.UpdateMedia("h-media", description: "Page 12",
            filePath: "metric book 1890.jpg", client: server.Client(), config: Config(), attachTo: Links("I0009")));

        Assert.Contains("there is no person I0009. Nothing was changed.", ex.Message);
        Assert.DoesNotContain(server.Requests, r => r.StartsWith("PUT", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Upload_Is_Refused_In_Read_Only_Mode()
    {
        var server = new FakeGramps();

        await Assert.ThrowsAsync<McpException>(() => MediaTools.CreateMedia("metric book 1890.jpg",
            client: server.Client(readOnly: true), config: Config()));

        Assert.DoesNotContain("POST /api/media/", server.Requests);
    }

    private static FlexibleHandleList Handles(params string[] values) =>
        JsonSerializer.Deserialize<FlexibleHandleList>(JsonSerializer.Serialize(values))!;

    private static FlexibleMediaLinkList Links(params string[] values) =>
        JsonSerializer.Deserialize<FlexibleMediaLinkList>(JsonSerializer.Serialize(values))!;

    private static FlexibleMediaLinkList LinksJson(string json) => JsonSerializer.Deserialize<FlexibleMediaLinkList>(json)!;

    private GrampsConfig Config() =>
        new("https://gramps-web.test", "user", "pass", "tree", MediaResourcesEnabled: true, MediaUploadDirs: [_uploads]);

    /// <summary>
    /// A Gramps Web stand-in: records each request as "METHOD path?query", serves <see cref="Objects"/> and
    /// <see cref="Lists"/>, stores PUT bodies in <see cref="Saved"/> and answers 404 to anything else.
    /// </summary>
    private sealed class FakeGramps : HttpMessageHandler
    {
        public Dictionary<string, string> Objects { get; } = new();

        public Dictionary<string, string> Lists { get; } = new();

        public Dictionary<string, JsonObject> Saved { get; } = new();

        public List<string> Requests { get; } = [];

        public string StoredByChecksum { get; init; } = "[]";

        public bool UploadTimesOut { get; init; }

        public byte[]? UploadBytes { get; private set; }

        public string? UploadContentType { get; private set; }

        public GrampsApiClient Client(bool readOnly = false)
        {
            // A distinct URL per server keeps the Gramps ID cache of one test out of the next.
            var config = new GrampsConfig($"https://{Guid.NewGuid():N}.test", "user", "pass", "tree", ReadOnly: readOnly);
            var http = new HttpClient(this);
            var tokens = new GrampsAuthTokenProvider(http, config, NullLogger<GrampsAuthTokenProvider>.Instance);
            return new GrampsApiClient(http, config, NullLogger<GrampsApiClient>.Instance, tokens);
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.PathAndQuery;
            if (path.StartsWith("/api/token/", StringComparison.Ordinal))
                return Json("""{"access_token":"token","refresh_token":"refresh","expires_in":900}""");
            Requests.Add($"{request.Method} {path}");

            if (request.Method == HttpMethod.Get)
            {
                if (path.StartsWith("/api/media/?gql=", StringComparison.Ordinal))
                    return Json(StoredByChecksum);
                if (Lists.TryGetValue(path, out var list))
                    return Json(list);
                return Objects.TryGetValue(path, out var stored) ? Json(stored) : NotFound();
            }

            if (path is "/api/media/" || path.EndsWith("/file", StringComparison.Ordinal))
            {
                if (UploadTimesOut)
                    throw new TaskCanceledException("timeout", new TimeoutException());
                UploadBytes = await request.Content!.ReadAsByteArrayAsync(cancellationToken);
                UploadContentType = request.Content.Headers.ContentType?.MediaType;
                var stored = new JsonObject
                {
                    ["_class"] = "Media", ["handle"] = "h-new", ["gramps_id"] = "O0042", ["desc"] = "",
                    ["checksum"] = MediaUploadFile.ChecksumOf(UploadBytes), ["mime"] = UploadContentType,
                    ["path"] = MediaUploadFile.ChecksumOf(UploadBytes) + ".jpg",
                };
                if (request.Method == HttpMethod.Post)
                {
                    Objects["/api/media/h-new"] = stored.ToJsonString();
                    return Json(new JsonArray(new JsonObject { ["_class"] = "Media", ["handle"] = "h-new", ["new"] = stored.DeepClone() }).ToJsonString(),
                        HttpStatusCode.Created);
                }

                var recordPath = path[..^"/file".Length];
                var record = JsonNode.Parse(Objects[recordPath])!.AsObject();
                foreach (var key in new[] { "checksum", "mime", "path" })
                    record[key] = stored[key]!.DeepClone();
                Objects[recordPath] = record.ToJsonString();
                return Json(new JsonArray().ToJsonString());
            }

            Assert.Equal(HttpMethod.Put, request.Method);
            var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!.AsObject();
            Saved[path] = body;
            Objects[path] = body.ToJsonString();
            return Json("[]");
        }

        private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
            new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

        private static HttpResponseMessage NotFound() =>
            new(HttpStatusCode.NotFound) { Content = new StringContent("""{"error":{"code":404,"message":"Not Found"}}""", Encoding.UTF8, "application/json") };
    }
}
