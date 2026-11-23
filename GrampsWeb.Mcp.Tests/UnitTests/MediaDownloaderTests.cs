using System.Net;
using GrampsWeb.Mcp.Config;
using GrampsWeb.Mcp.Input;
using GrampsWeb.Mcp.Tools;
using ModelContextProtocol;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

/// <summary>
/// Downloading a file attached in the chat: which links are fetched, how redirects, errors and the size limit
/// are handled, and that messages name the host but never the signed link.
/// </summary>
public class MediaDownloaderTests
{
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 1, 2, 3];

    private const string Link = "https://files.example.com/download/file-1?sig=SECRET-SIGNATURE";

    [Fact]
    public async Task Downloads_An_Attached_File_And_Judges_It_By_Content()
    {
        var web = new FakeWeb { [Link] = () => Ok(Jpeg, "application/octet-stream") };

        var upload = await Downloader(web).ReadAsync(File(Link, name: "scans/metric book 1890.png", mime: "image/png"), Config());

        Assert.Equal("metric book 1890.png", upload.Path);
        Assert.Equal("metric book 1890", upload.DefaultDescription);
        Assert.Equal("image/jpeg", upload.MimeType);
        Assert.Equal(Jpeg, upload.Bytes);
        Assert.Equal(MediaUploadFile.ChecksumOf(Jpeg), upload.Checksum);
        Assert.Equal([Link], web.Requested);
    }

    [Fact]
    public async Task Sends_No_Credentials_Or_Cookies()
    {
        var web = new FakeWeb { [Link] = () => Ok(Jpeg) };

        await Downloader(web).ReadAsync(File(Link), Config());

        var request = Assert.Single(web.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Null(request.Headers.Authorization);
        Assert.False(request.Headers.Contains("Cookie"));
        Assert.NotEmpty(request.Headers.UserAgent);
    }

    [Theory]
    [InlineData("", "file-1")]
    [InlineData("  ", "file-1")]
    [InlineData("../", "file-1")]
    [InlineData("C:\\Users\\me\\scan\u0007.jpg", "scan.jpg")]
    public async Task Names_The_Upload_By_The_File_Name_Without_Folders_Else_By_Its_ID(string fileName, string expected)
    {
        var web = new FakeWeb { [Link] = () => Ok(Jpeg) };

        var upload = await Downloader(web).ReadAsync(File(Link, name: fileName), Config());

        Assert.Equal(expected, upload.Path);
    }

    [Fact]
    public async Task Allows_Subdomains_Of_An_Allowed_Host()
    {
        const string link = "https://eu.files.example.com/f/1";
        var web = new FakeWeb { [link] = () => Ok(Jpeg) };

        var upload = await Downloader(web).ReadAsync(File(link), Config());

        Assert.Equal(Jpeg, upload.Bytes);
    }

    [Theory]
    [InlineData("oaisdmntprnortheu.blob.core.windows.net", true)]
    [InlineData("oaisdmntprdenmarkeast.blob.core.windows.net", true)]
    [InlineData("oaisdmntpr.blob.core.windows.net", true)]
    [InlineData("eu.oaisdmntprnortheu.blob.core.windows.net", true)]
    [InlineData("files.example.com", true)]
    [InlineData("evil.blob.core.windows.net", false)]
    [InlineData("evil-oaisdmntpr.blob.core.windows.net", false)]
    [InlineData("oaisdmntprx.evil.blob.core.windows.net", false)]
    [InlineData("blob.core.windows.net", false)]
    [InlineData("oaisdmntprx.blob.core.windows.net.evil.test", false)]
    [InlineData("oaisdmntprx.blob.core.windows.net-evil.test", false)]
    public void Matches_A_Star_Only_Within_The_First_Label(string host, bool allowed)
    {
        Assert.Equal(allowed,
            MediaDownloader.IsAllowedHost(host, ["files.example.com", "oaisdmntpr*.blob.core.windows.net"]));
    }

    [Theory]
    [InlineData("files-eu.example.com", true)]
    [InlineData("eu-files.example.com", false)]
    [InlineData("eu-files.example.org", true)]
    [InlineData("files-eu.example.org", false)]
    public void Matches_A_Star_At_Either_End_Of_The_Label(string host, bool allowed)
    {
        Assert.Equal(allowed, MediaDownloader.IsAllowedHost(host, ["files-*.example.com", "*-files.example.org"]));
    }

    [Fact]
    public async Task Downloads_From_A_Host_That_Matches_A_Pattern()
    {
        const string link = "https://oaisdmntprnortheu.blob.core.windows.net/files/1?sig=x";
        var web = new FakeWeb { [link] = () => Ok(Jpeg) };
        var config = Config() with { MediaUploadHosts = ["oaisdmntpr*.blob.core.windows.net"] };

        var upload = await Downloader(web).ReadAsync(File(link), config);

        Assert.Equal(Jpeg, upload.Bytes);
    }

    [Theory]
    [InlineData("https://evil.test/f/1?sig=SECRET-SIGNATURE", "evil.test")]
    [InlineData("https://evilfiles.example.com/f/1", "evilfiles.example.com")]
    [InlineData("https://files.example.com.evil.test/f/1", "files.example.com.evil.test")]
    [InlineData("https://203.0.113.7/f/1", "203.0.113.7")]
    public async Task Refuses_Hosts_The_Setting_Does_Not_Allow(string link, string host)
    {
        var web = new FakeWeb();

        var ex = await Refused(web, File(link));

        Assert.Contains($"a link on {host}, which GRAMPS_MEDIA_UPLOAD_HOSTS does not allow (files.example.com)", ex.Message);
        Assert.DoesNotContain("SECRET", ex.Message);
        Assert.Empty(web.Requested);
    }

    [Theory]
    [InlineData("http://files.example.com/f/1", "only HTTPS links")]
    [InlineData("ftp://files.example.com/f/1", "only HTTPS links")]
    [InlineData("https://user:pass@files.example.com/f/1", "user name or password")]
    [InlineData("file-1", "download_url is not a link")]
    public async Task Refuses_Links_That_Are_Not_Plain_HTTPS(string link, string message)
    {
        var web = new FakeWeb();

        var ex = await Refused(web, File(link));

        Assert.Contains(message, ex.Message);
        Assert.Empty(web.Requested);
    }

    [Fact]
    public async Task Requires_A_Download_Link()
    {
        var ex = await Refused(new FakeWeb(), File(""));

        Assert.Contains("file needs download_url", ex.Message);
    }

    [Fact]
    public async Task Follows_Redirects_Within_The_Allowed_Hosts()
    {
        var web = new FakeWeb
        {
            [Link] = () => Redirect("https://cdn.files.example.com/blob/1"),
            ["https://cdn.files.example.com/blob/1"] = () => Redirect("/blob/2", HttpStatusCode.TemporaryRedirect),
            ["https://cdn.files.example.com/blob/2"] = () => Ok(Jpeg),
        };

        var upload = await Downloader(web).ReadAsync(File(Link), Config());

        Assert.Equal(Jpeg, upload.Bytes);
        Assert.Equal(3, web.Requested.Count);
    }

    [Fact]
    public async Task Refuses_A_Redirect_To_Another_Host()
    {
        var web = new FakeWeb { [Link] = () => Redirect("https://metadata.internal.test/latest") };

        var ex = await Refused(web, File(Link));

        Assert.Contains("redirects to a link on metadata.internal.test", ex.Message);
        Assert.Equal([Link], web.Requested);
    }

    [Fact]
    public async Task Refuses_A_Redirect_To_Plain_HTTP()
    {
        var web = new FakeWeb { [Link] = () => Redirect("http://files.example.com/f/1") };

        var ex = await Refused(web, File(Link));

        Assert.Contains("redirects to a http link", ex.Message);
    }

    [Fact]
    public async Task Stops_After_Three_Redirects()
    {
        var web = new FakeWeb
        {
            [Link] = () => Redirect("https://files.example.com/1"),
            ["https://files.example.com/1"] = () => Redirect("https://files.example.com/2"),
            ["https://files.example.com/2"] = () => Redirect("https://files.example.com/3"),
            ["https://files.example.com/3"] = () => Redirect("https://files.example.com/4"),
        };

        var ex = await Refused(web, File(Link));

        Assert.Contains($"redirected more than {MediaDownloader.MaxRedirects} times", ex.Message);
        Assert.Equal(4, web.Requested.Count);
    }

    [Fact]
    public async Task Says_The_Link_May_Have_Expired_Without_Showing_It()
    {
        var web = new FakeWeb { [Link] = () => new HttpResponseMessage(HttpStatusCode.Forbidden) { ReasonPhrase = "Forbidden" } };

        var ex = await Refused(web, File(Link, name: "scan.jpg"));

        Assert.Contains("Downloading scan.jpg failed: files.example.com answered 403 Forbidden", ex.Message);
        Assert.Contains("attach the file in the chat again", ex.Message);
        Assert.DoesNotContain("SECRET", ex.Message);
    }

    [Fact]
    public async Task Reports_A_Failed_Request_By_Host()
    {
        var web = new FakeWeb { [Link] = () => throw new HttpRequestException("No such host is known.") };

        var ex = await Refused(web, File(Link, name: "scan.jpg"));

        Assert.Equal("Downloading scan.jpg from files.example.com failed: No such host is known.", ex.Message);
    }

    [Fact]
    public async Task Refuses_A_File_Over_The_Limit_By_Its_Length()
    {
        var web = new FakeWeb { [Link] = () => Ok(new byte[101]) };

        var ex = await Refused(web, File(Link, name: "scan.jpg"), Config(maxBytes: 100));

        Assert.Contains("scan.jpg is 101 bytes, more than the upload limit of 100 bytes", ex.Message);
    }

    [Fact]
    public async Task Refuses_A_File_Over_The_Limit_While_Reading_It()
    {
        var web = new FakeWeb
        {
            [Link] = () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new UnknownLengthStream(new byte[101])) },
        };

        var ex = await Refused(web, File(Link, name: "scan.jpg"), Config(maxBytes: 100));

        Assert.Contains("scan.jpg is more than the upload limit of 100 bytes", ex.Message);
    }

    [Fact]
    public async Task Refuses_An_Empty_File()
    {
        var web = new FakeWeb { [Link] = () => Ok([]) };

        var ex = await Refused(web, File(Link, name: "scan.jpg"));

        Assert.Equal("scan.jpg is empty.", ex.Message);
    }

    [Fact]
    public async Task Refuses_Other_Types_Whatever_The_Client_Says()
    {
        var web = new FakeWeb { [Link] = () => Ok("<html></html>"u8.ToArray(), "image/jpeg") };

        var ex = await Refused(web, File(Link, name: "scan.jpg", mime: "image/jpeg"));

        Assert.Contains("scan.jpg is not a JPEG, PNG, GIF, WebP, TIFF, BMP or PDF file (judged by its content, not its name)", ex.Message);
    }

    [Fact]
    public async Task Says_How_To_Turn_Downloads_On()
    {
        var ex = await Refused(new FakeWeb(), File(Link), new GrampsConfig("https://gramps-web.test", "user", "pass", "tree",
            MediaResourcesEnabled: true, MediaUploadDirs: ["/srv/uploads"]));

        Assert.Contains("Uploading a file attached in the chat is turned off: set GRAMPS_MEDIA_UPLOAD_HOSTS", ex.Message);
        Assert.Contains("goes in filePath instead", ex.Message);
    }

    [Fact]
    public async Task Needs_Media_Access_As_Well()
    {
        var ex = await Refused(new FakeWeb(), File(Link), new GrampsConfig("https://gramps-web.test", "user", "pass", "tree",
            MediaUploadHosts: ["files.example.com"]));

        Assert.Contains("media file access is disabled; set GRAMPS_MEDIA_RESOURCES_ENABLED=true", ex.Message);
    }

    [Theory]
    [InlineData("8.8.8.8", true)]
    [InlineData("1.1.1.1", true)]
    [InlineData("172.15.255.255", true)]
    [InlineData("172.32.0.1", true)]
    [InlineData("100.128.0.1", true)]
    [InlineData("::ffff:8.8.8.8", true)]
    [InlineData("2606:4700:4700::1111", true)]
    [InlineData("2002:808:808::1", true)]
    [InlineData("64:ff9b::808:808", true)]
    [InlineData("0.0.0.0", false)]
    [InlineData("10.1.2.3", false)]
    [InlineData("100.64.0.1", false)]
    [InlineData("127.0.0.1", false)]
    [InlineData("169.254.169.254", false)]
    [InlineData("172.16.0.1", false)]
    [InlineData("172.31.255.255", false)]
    [InlineData("192.0.0.1", false)]
    [InlineData("192.0.2.1", false)]
    [InlineData("192.168.1.10", false)]
    [InlineData("198.18.0.1", false)]
    [InlineData("198.51.100.1", false)]
    [InlineData("203.0.113.1", false)]
    [InlineData("224.0.0.1", false)]
    [InlineData("255.255.255.255", false)]
    [InlineData("::", false)]
    [InlineData("::1", false)]
    [InlineData("::ffff:127.0.0.1", false)]
    [InlineData("::ffff:192.168.1.10", false)]
    [InlineData("fe80::1", false)]
    [InlineData("fc00::1", false)]
    [InlineData("fd12:3456::1", false)]
    [InlineData("ff02::1", false)]
    [InlineData("2001:db8::1", false)]
    [InlineData("2002:7f00:1::1", false)]
    [InlineData("2002:a00:1::1", false)]
    [InlineData("64:ff9b::a00:1", false)]
    public void Connects_Only_To_Public_Addresses(string address, bool expected)
    {
        Assert.Equal(expected, MediaDownloader.IsPublicAddress(IPAddress.Parse(address)));
    }

    private static MediaDownloader Downloader(FakeWeb web) => new(web);

    private static async Task<McpException> Refused(FakeWeb web, FileReference file, GrampsConfig? config = null) =>
        await Assert.ThrowsAsync<McpException>(() => Downloader(web).ReadAsync(file, config ?? Config()));

    private static FileReference File(string link, string? name = null, string? mime = null) =>
        new() { DownloadUrl = link, FileId = "file-1", FileName = name, MimeType = mime };

    private static GrampsConfig Config(long maxBytes = GrampsConfig.DefaultMediaUploadMaxBytes) =>
        new("https://gramps-web.test", "user", "pass", "tree", MediaResourcesEnabled: true,
            MediaUploadMaxBytes: maxBytes, MediaUploadHosts: ["files.example.com"]);

    private static HttpResponseMessage Ok(byte[] bytes, string? type = null)
    {
        var content = new ByteArrayContent(bytes);
        if (type != null)
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(type);
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    /// <summary>A redirect whose Location header goes through HttpClient's own parsing, as a server's would.</summary>
    private static HttpResponseMessage Redirect(string location, HttpStatusCode status = HttpStatusCode.Found)
    {
        var response = new HttpResponseMessage(status);
        Assert.True(response.Headers.TryAddWithoutValidation("Location", location));
        return response;
    }

    /// <summary>Serves the responses registered for absolute links and records each request.</summary>
    private sealed class FakeWeb : HttpMessageHandler
    {
        private readonly Dictionary<string, Func<HttpResponseMessage>> _responses = new();

        public List<HttpRequestMessage> Requests { get; } = [];

        public List<string> Requested => Requests.Select(request => request.RequestUri!.AbsoluteUri).ToList();

        public Func<HttpResponseMessage> this[string link]
        {
            set => _responses[link] = value;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(_responses.TryGetValue(request.RequestUri!.AbsoluteUri, out var respond)
                ? respond()
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    /// <summary>A stream that does not tell its length, so the response has no Content-Length.</summary>
    private sealed class UnknownLengthStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
    }
}
