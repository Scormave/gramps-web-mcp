using GrampsWeb.Mcp.Config;
using GrampsWeb.Mcp.Tools;
using ModelContextProtocol;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

public class MediaUploadFileTests : IDisposable
{
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, (byte)'J', (byte)'F', (byte)'I', (byte)'F'];

    private readonly string _root = Path.Combine(Path.GetTempPath(), "gramps-upload-tests-" + Guid.NewGuid().ToString("N"));
    private readonly string _uploads;
    private readonly string _outside;

    public MediaUploadFileTests()
    {
        _uploads = Directory.CreateDirectory(Path.Combine(_root, "uploads")).FullName;
        _outside = Directory.CreateDirectory(Path.Combine(_root, "outside")).FullName;
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Theory]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xDB }, "image/jpeg")]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00 }, "image/png")]
    [InlineData(new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61 }, "image/gif")]
    [InlineData(new byte[] { 0x52, 0x49, 0x46, 0x46, 0x10, 0, 0, 0, 0x57, 0x45, 0x42, 0x50 }, "image/webp")]
    [InlineData(new byte[] { 0x49, 0x49, 0x2A, 0x00 }, "image/tiff")]
    [InlineData(new byte[] { 0x4D, 0x4D, 0x00, 0x2A }, "image/tiff")]
    [InlineData(new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x37 }, "application/pdf")]
    [InlineData(new byte[] { 0x42, 0x4D, 0, 0, 0, 0, 0, 0, 0, 0, 0x36, 0, 0, 0, 40, 0, 0, 0 }, "image/bmp")]
    public void SniffMimeType_Recognises_Supported_Types(byte[] bytes, string expected) =>
        Assert.Equal(expected, MediaUploadFile.SniffMimeType(bytes));

    [Theory]
    [InlineData(new byte[] { 0x42, 0x4D, 0x20, 0x6E, 0x6F, 0x74, 0x65 })] // "BM note"
    [InlineData(new byte[] { 0x52, 0x49, 0x46, 0x46, 0x10, 0, 0, 0, 0x57, 0x41, 0x56, 0x45 })] // RIFF WAVE
    [InlineData(new byte[] { 0x3C, 0x73, 0x76, 0x67 })] // "<svg"
    [InlineData(new byte[] { 0xFF, 0xD8 })]
    public void SniffMimeType_Rejects_Other_Content(byte[] bytes) =>
        Assert.Null(MediaUploadFile.SniffMimeType(bytes));

    [Fact]
    public void ChecksumOf_Is_Lowercase_Hex_Md5() =>
        Assert.Equal("900150983cd24fb0d6963f7d28e17f72", MediaUploadFile.ChecksumOf("abc"u8.ToArray()));

    [Fact]
    public async Task ReadAsync_Finds_A_Relative_Name_In_An_Upload_Folder()
    {
        var other = Directory.CreateDirectory(Path.Combine(_root, "other")).FullName;
        File.WriteAllBytes(Path.Combine(_uploads, "scan 1.jpg"), Jpeg);

        var file = await MediaUploadFile.ReadAsync("scan 1.jpg", Config(other, _uploads));

        Assert.Equal(MediaUploadFile.RealPath(Path.Combine(_uploads, "scan 1.jpg")), file.Path);
        Assert.Equal(Jpeg, file.Bytes);
        Assert.Equal("image/jpeg", file.MimeType);
        Assert.Equal(MediaUploadFile.ChecksumOf(Jpeg), file.Checksum);
        Assert.Equal("scan 1", file.DefaultDescription);
    }

    [Fact]
    public async Task ReadAsync_Accepts_An_Absolute_Path_Inside_A_Folder_And_Judges_Type_By_Content()
    {
        var path = Path.Combine(_uploads, "nested", "letter.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, Jpeg);

        var file = await MediaUploadFile.ReadAsync(path, Config(_uploads));

        Assert.Equal("image/jpeg", file.MimeType);
    }

    [Fact]
    public async Task ReadAsync_Rejects_A_File_Outside_The_Folders()
    {
        var path = Path.Combine(_outside, "secret.jpg");
        File.WriteAllBytes(path, Jpeg);

        var ex = await Assert.ThrowsAsync<McpException>(() => MediaUploadFile.ReadAsync(path, Config(_uploads)));
        Assert.Contains("outside the upload folders", ex.Message);
    }

    [Fact]
    public async Task ReadAsync_Rejects_Climbing_Out_With_Dot_Dot()
    {
        File.WriteAllBytes(Path.Combine(_outside, "secret.jpg"), Jpeg);

        var ex = await Assert.ThrowsAsync<McpException>(
            () => MediaUploadFile.ReadAsync(Path.Combine("..", "outside", "secret.jpg"), Config(_uploads)));
        Assert.Contains("outside the upload folders", ex.Message);
    }

    [Fact]
    public async Task ReadAsync_Rejects_A_Folder_Whose_Name_Only_Starts_Like_An_Upload_Folder()
    {
        var sibling = Directory.CreateDirectory(_uploads + "-old").FullName;
        var path = Path.Combine(sibling, "scan.jpg");
        File.WriteAllBytes(path, Jpeg);

        var ex = await Assert.ThrowsAsync<McpException>(() => MediaUploadFile.ReadAsync(path, Config(_uploads)));
        Assert.Contains("outside the upload folders", ex.Message);
    }

    [Fact]
    public async Task ReadAsync_Rejects_A_Link_That_Leads_Outside_The_Folders()
    {
        if (OperatingSystem.IsWindows())
            return; // Creating symbolic links needs extra rights on Windows.
        File.WriteAllBytes(Path.Combine(_outside, "secret.jpg"), Jpeg);
        File.CreateSymbolicLink(Path.Combine(_uploads, "innocent.jpg"), Path.Combine(_outside, "secret.jpg"));
        Directory.CreateSymbolicLink(Path.Combine(_uploads, "folder"), _outside);

        var file = await Assert.ThrowsAsync<McpException>(() => MediaUploadFile.ReadAsync("innocent.jpg", Config(_uploads)));
        var folder = await Assert.ThrowsAsync<McpException>(
            () => MediaUploadFile.ReadAsync(Path.Combine(_uploads, "folder", "secret.jpg"), Config(_uploads)));

        Assert.Contains("outside the upload folders", file.Message);
        Assert.Contains("outside the upload folders", folder.Message);
    }

    [Fact]
    public async Task ReadAsync_Follows_A_Link_That_Stays_Inside()
    {
        if (OperatingSystem.IsWindows())
            return;
        File.WriteAllBytes(Path.Combine(_uploads, "real.jpg"), Jpeg);
        File.CreateSymbolicLink(Path.Combine(_uploads, "alias.jpg"), "real.jpg");

        var file = await MediaUploadFile.ReadAsync("alias.jpg", Config(_uploads));

        Assert.Equal(MediaUploadFile.RealPath(Path.Combine(_uploads, "real.jpg")), file.Path);
    }

    [Fact]
    public async Task ReadAsync_Rejects_Missing_Empty_Too_Large_And_Unknown_Files()
    {
        File.WriteAllBytes(Path.Combine(_uploads, "empty.jpg"), []);
        File.WriteAllBytes(Path.Combine(_uploads, "big.jpg"), [.. Jpeg, .. new byte[100]]);
        File.WriteAllText(Path.Combine(_uploads, "notes.jpg"), "plain text");
        Directory.CreateDirectory(Path.Combine(_uploads, "scans"));
        var config = Config(_uploads) with { MediaUploadMaxBytes = 50 };

        Assert.Contains("does not exist", (await Assert.ThrowsAsync<McpException>(() => MediaUploadFile.ReadAsync("missing.jpg", config))).Message);
        Assert.Contains("is empty", (await Assert.ThrowsAsync<McpException>(() => MediaUploadFile.ReadAsync("empty.jpg", config))).Message);
        Assert.Contains("GRAMPS_MEDIA_UPLOAD_MAX_BYTES", (await Assert.ThrowsAsync<McpException>(() => MediaUploadFile.ReadAsync("big.jpg", config))).Message);
        Assert.Contains("judged by its content", (await Assert.ThrowsAsync<McpException>(() => MediaUploadFile.ReadAsync("notes.jpg", config))).Message);
        Assert.Contains("is a folder", (await Assert.ThrowsAsync<McpException>(() => MediaUploadFile.ReadAsync("scans", config))).Message);
    }

    [Fact]
    public async Task ReadAsync_Explains_When_Upload_Is_Off()
    {
        var config = new GrampsConfig("https://gramps-web.test", "user", "pass", "tree");

        var ex = await Assert.ThrowsAsync<McpException>(() => MediaUploadFile.ReadAsync("scan.jpg", config));

        Assert.Contains("GRAMPS_MEDIA_UPLOAD_DIRS", ex.Message);
        Assert.Contains("GRAMPS_MEDIA_RESOURCES_ENABLED=true", ex.Message);
    }

    [Fact]
    public async Task ReadAsync_Asks_Only_For_A_Folder_When_Media_Access_Is_On()
    {
        var config = new GrampsConfig("https://gramps-web.test", "user", "pass", "tree", MediaResourcesEnabled: true);

        var ex = await Assert.ThrowsAsync<McpException>(() => MediaUploadFile.ReadAsync("scan.jpg", config));

        Assert.Contains("GRAMPS_MEDIA_UPLOAD_DIRS", ex.Message);
        Assert.DoesNotContain("GRAMPS_MEDIA_RESOURCES_ENABLED", ex.Message);
    }

    [Fact]
    public async Task ReadAsync_Refuses_Upload_Folders_Without_Media_Access()
    {
        File.WriteAllBytes(Path.Combine(_uploads, "scan.jpg"), Jpeg);
        var config = Config(_uploads) with { MediaResourcesEnabled = false };

        var ex = await Assert.ThrowsAsync<McpException>(() => MediaUploadFile.ReadAsync("scan.jpg", config));

        Assert.Contains("media file access is disabled", ex.Message);
        Assert.Contains("GRAMPS_MEDIA_RESOURCES_ENABLED=true", ex.Message);
    }

    [Fact]
    public async Task ReadAsync_Explains_When_No_Upload_Folder_Exists()
    {
        var ex = await Assert.ThrowsAsync<McpException>(
            () => MediaUploadFile.ReadAsync("scan.jpg", Config(Path.Combine(_root, "not-there"))));

        Assert.Contains("None of the upload folders exists", ex.Message);
    }

    private static GrampsConfig Config(params string[] directories) =>
        new("https://gramps-web.test", "user", "pass", "tree", MediaResourcesEnabled: true, MediaUploadDirs: directories);
}
