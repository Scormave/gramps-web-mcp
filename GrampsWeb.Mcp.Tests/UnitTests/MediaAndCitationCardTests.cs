using System.Net;
using System.Text.Json;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Config;
using GrampsWeb.Mcp.Formatters;
using GrampsWeb.Mcp.Models;
using GrampsWeb.Mcp.Serialization;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

public class MediaAndCitationCardTests
{
    private const string EmptyDate = """{"calendar":0,"modifier":0,"quality":0,"dateval":[0,0,0,false],"text":"","sortval":0,"newyear":0}""";

    [Fact]
    public void MediaCard_Leaves_Out_An_Empty_Date_And_The_Thumbnail_Of_A_Pdf()
    {
        var media = Media("application/pdf", EmptyDate);

        var result = MediaFormatter.FormatMediaFull(media).Replace("\r\n", "\n");

        Assert.DoesNotContain("Date:", result);
        Assert.DoesNotContain("Unknown date", result);
        Assert.DoesNotContain("thumbnail resource", result);
        Assert.DoesNotContain("defaults to mode thumbnail", result);
        Assert.Contains(
            "  file resource: gramps://media/m1/file\n" +
            "  read_media needs mode file: thumbnails are rendered only from images, and this record is application/pdf\n",
            result);
    }

    [Theory]
    [InlineData("image/jpeg")]
    [InlineData("")]
    public void MediaCard_Offers_The_Thumbnail_Of_An_Image_Or_An_Unknown_Type(string mime)
    {
        var media = Media(mime, """{"modifier":0,"dateval":[21,8,1930,false]}""");

        var result = MediaFormatter.FormatMediaFull(media).Replace("\r\n", "\n");

        Assert.Contains("\nDate: 21 Aug 1930\n", result);
        Assert.Contains("  thumbnail resource: gramps://media/m1/thumbnail/1568\n", result);
        Assert.DoesNotContain("read_media needs mode file", result);
    }

    [Fact]
    public async Task CitationCard_Leaves_Out_An_Empty_Date()
    {
        var citation = JsonSerializer.Deserialize<GrampsCitation>(
            $$"""{"handle":"c1","gramps_id":"C0001","page":"p. 12","confidence":2,"date":{{EmptyDate}}}""",
            GrampsJson.Options)!;

        var result = await CitationFormatter.FormatCitationFull(citation, CreateClient());

        Assert.Contains("Page/Location: p. 12", result);
        Assert.DoesNotContain("Access Date", result);
        Assert.DoesNotContain("Unknown date", result);
    }

    private static GrampsMedia Media(string mime, string date) =>
        JsonSerializer.Deserialize<GrampsMedia>(
            $$"""{"handle":"m1","gramps_id":"O0001","path":"scans/record.pdf","mime":"{{mime}}","date":{{date}}}""",
            GrampsJson.Options)!;

    private static GrampsApiClient CreateClient()
    {
        var config = new GrampsConfig("https://gramps-web.test", "user", "pass", "tree");
        var http = new HttpClient(new NotFoundHandler());
        return new GrampsApiClient(http, config, NullLogger<GrampsApiClient>.Instance,
            new GrampsAuthTokenProvider(http, config, NullLogger<GrampsAuthTokenProvider>.Instance));
    }

    private sealed class NotFoundHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}
