using ModelContextProtocol.Protocol;
using Xunit;

namespace GrampsWeb.Mcp.IntegrationTests;

/// <summary>Uploading media files from the upload folder and reading them back.</summary>
[Collection(GrampsWebCollection.Name)]
public sealed class MediaTests(GrampsWebFixture gramps)
{
    private McpSession Session => gramps.Session;

    [IntegrationFact]
    public async Task CreateMedia_UploadsFileAndAttachesIt()
    {
        var person = await Session.CreateAsync("create_person", new { primaryName = new { first = "Ivan", surname = TestNames.Surname() } });
        var file = gramps.WriteUpload(".png", TestImages.Png());

        var media = await Session.CreateAsync("create_media", new
        {
            filePath = file,
            description = "Birth record",
            attachTo = new[] { person.GrampsId }
        });

        Assert.Equal("created", media.Action);
        Assert.Equal("\"image/png\"", McpSession.Header(media.Text, "mime"));
        Assert.Equal($"[\"{person.GrampsId}\"]", McpSession.Header(media.Text, "attached_to"));
        var stored = await Session.CallAsync("get_object", new { identifier = person.GrampsId });
        Assert.Contains($"[image] Birth record", stored);
        Assert.Contains(media.Handle, stored);
    }

    [IntegrationFact]
    public async Task CreateMedia_SameFileAgain_ReusesTheRecord()
    {
        var bytes = TestImages.Png();
        var first = await Session.CreateAsync("create_media", new { filePath = gramps.WriteUpload(".png", bytes) });

        var second = await Session.CreateAsync("create_media", new { filePath = gramps.WriteUpload(".png", bytes) });

        Assert.Equal("existing", second.Action);
        Assert.Equal(first.Handle, second.Handle);
        Assert.Contains("same MD5 checksum", second.Text);
    }

    [IntegrationFact]
    public async Task CreateMedia_AttachesRegion()
    {
        var person = await Session.CreateAsync("create_person", new { primaryName = new { first = "Anna", surname = TestNames.Surname() } });

        var media = await Session.CreateAsync("create_media", new
        {
            filePath = gramps.WriteUpload(".png", TestImages.Png()),
            attachTo = new object[] { new { @ref = person.GrampsId, region = new[] { 10, 20, 60, 80 } } }
        });

        var stored = await Session.CallAsync("get_object", new { identifier = person.GrampsId });
        Assert.Contains(media.Handle, stored);
        Assert.Contains("10, 20, 60, 80", stored);
    }

    [IntegrationFact]
    public async Task CreateMedia_OutsideUploadFolder_IsRefused()
    {
        var outside = Path.Combine(Path.GetTempPath(), TestNames.Unique("outside") + ".png");
        await File.WriteAllBytesAsync(outside, TestImages.Png());
        try
        {
            var error = await Session.CallErrorAsync("create_media", new { filePath = outside });

            Assert.Contains("is outside the upload folders", error);
        }
        finally
        {
            File.Delete(outside);
        }
    }

    [IntegrationFact]
    public async Task ReadMedia_ReturnsThumbnailFileAndRegion()
    {
        var bytes = TestImages.Png(width: 200, height: 120);
        var media = await Session.CreateAsync("create_media", new { filePath = gramps.WriteUpload(".png", bytes) });

        var thumbnail = await ReadImageAsync(new { handle = media.Handle });
        Assert.Equal("image/jpeg", thumbnail.MimeType);

        var file = await ReadImageAsync(new { handle = media.Handle, mode = "file" });
        Assert.Equal("image/png", file.MimeType);
        Assert.Equal(bytes, file.DecodedData.ToArray());

        var region = await ReadImageAsync(new { handle = media.Handle, region = new[] { 0, 0, 50, 50 } });
        Assert.Equal("image/jpeg", region.MimeType);
    }

    [IntegrationFact]
    public async Task MediaFileResource_ReturnsOriginalBytes()
    {
        var bytes = TestImages.Png();
        var media = await Session.CreateAsync("create_media", new { filePath = gramps.WriteUpload(".png", bytes) });

        var resource = await Session.Client.ReadResourceAsync($"gramps://media/{media.Handle}/file");

        var blob = Assert.IsType<BlobResourceContents>(Assert.Single(resource.Contents));
        Assert.Equal("image/png", blob.MimeType);
        Assert.Equal(bytes, blob.DecodedData.ToArray());
    }

    [IntegrationFact]
    public async Task UpdateMedia_ReplacesTheFile()
    {
        var media = await Session.CreateAsync("create_media", new { filePath = gramps.WriteUpload(".png", TestImages.Png()), description = "Scan" });
        var replacement = TestImages.Png(width: 32, height: 32);

        await Session.CallAsync("update_media", new { handle = media.Handle, filePath = gramps.WriteUpload(".png", replacement) });

        var file = await ReadImageAsync(new { handle = media.Handle, mode = "file" });
        Assert.Equal(replacement, file.DecodedData.ToArray());
        var stored = await Session.CallAsync("get_object", new { identifier = media.GrampsId });
        Assert.Contains("Scan", stored);
    }

    private async Task<ImageContentBlock> ReadImageAsync(object arguments)
    {
        var result = await Session.CallToolAsync("read_media", arguments);
        Assert.False(result.IsError == true, McpSession.TextOf(result));
        return Assert.Single(result.Content.OfType<ImageContentBlock>());
    }
}
