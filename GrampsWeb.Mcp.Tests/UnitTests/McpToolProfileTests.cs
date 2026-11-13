using GrampsWeb.Mcp.Hosting;
using ModelContextProtocol.Protocol;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

public class McpToolProfileTests
{
    [Fact]
    public void KeepAvailableTools_RemovesWriteToolsInReadOnlyMode()
    {
        var result = new ListToolsResult
        {
            Tools =
            [
                Tool("get_records", readOnly: true),
                Tool("create_records", readOnly: false),
                Tool("unannotated", readOnly: null)
            ]
        };

        var filtered = McpToolProfileExtensions.KeepAvailableTools(result, Config(readOnly: true, mediaResourcesEnabled: true));

        Assert.Same(result, filtered);
        var tool = Assert.Single(filtered.Tools);
        Assert.Equal("get_records", tool.Name);
    }

    [Fact]
    public void KeepAvailableTools_RemovesMediaByteToolsWhenDisabled()
    {
        var result = new ListToolsResult
        {
            Tools =
            [
                Tool("get_records", readOnly: true),
                Tool("get_media_image", readOnly: true),
                Tool("update_media", readOnly: false)
            ]
        };

        var filtered = McpToolProfileExtensions.KeepAvailableTools(result, Config(readOnly: false, mediaResourcesEnabled: false));

        Assert.Equal(["get_records", "update_media"], filtered.Tools.Select(tool => tool.Name));
    }

    [Fact]
    public void KeepAvailableTools_AppliesReadOnlyAndMediaFiltersTogether()
    {
        var result = new ListToolsResult
        {
            Tools =
            [
                Tool("get_records", readOnly: true),
                Tool("get_media_image", readOnly: true),
                Tool("create_records", readOnly: false)
            ]
        };

        var filtered = McpToolProfileExtensions.KeepAvailableTools(result, Config(readOnly: true, mediaResourcesEnabled: false));

        Assert.Equal(["get_records"], filtered.Tools.Select(tool => tool.Name));
    }

    [Theory]
    [InlineData(false, new[] { "get_records", "update_media" })]
    [InlineData(true, new[] { "get_records", "upload_media", "update_media" })]
    public void KeepAvailableTools_PublishesUploadMediaOnlyWithUploadFolders(bool uploadFolders, string[] expected)
    {
        var result = new ListToolsResult
        {
            Tools =
            [
                Tool("get_records", readOnly: true),
                Tool("upload_media", readOnly: false),
                Tool("update_media", readOnly: false)
            ]
        };

        var filtered = McpToolProfileExtensions.KeepAvailableTools(result,
            Config(readOnly: false, mediaResourcesEnabled: true, uploadFolders: uploadFolders));

        Assert.Equal(expected, filtered.Tools.Select(tool => tool.Name));
    }

    [Fact]
    public void KeepAvailableTools_ReadOnlyModeHidesUploadMediaEvenWithUploadFolders()
    {
        var result = new ListToolsResult { Tools = [Tool("get_records", readOnly: true), Tool("upload_media", readOnly: false)] };

        var filtered = McpToolProfileExtensions.KeepAvailableTools(result,
            Config(readOnly: true, mediaResourcesEnabled: true, uploadFolders: true));

        Assert.Equal(["get_records"], filtered.Tools.Select(tool => tool.Name));
    }

    [Fact]
    public void KeepAvailableTools_DisabledMediaAccessHidesUploadMediaEvenWithUploadFolders()
    {
        var result = new ListToolsResult
        {
            Tools =
            [
                Tool("get_records", readOnly: true),
                Tool("get_media_image", readOnly: true),
                Tool("upload_media", readOnly: false),
                Tool("update_media", readOnly: false)
            ]
        };

        var filtered = McpToolProfileExtensions.KeepAvailableTools(result,
            Config(readOnly: false, mediaResourcesEnabled: false, uploadFolders: true));

        Assert.Equal(["get_records", "update_media"], filtered.Tools.Select(tool => tool.Name));
    }

    private static GrampsWeb.Mcp.Config.GrampsConfig Config(bool readOnly, bool mediaResourcesEnabled, bool uploadFolders = false) => new(
        ApiUrl: "https://gramps-web.test",
        Username: "user",
        Password: "pass",
        TreeId: "tree",
        ReadOnly: readOnly,
        MediaResourcesEnabled: mediaResourcesEnabled,
        MediaUploadDirs: uploadFolders ? ["/srv/uploads"] : null);

    private static Tool Tool(string name, bool? readOnly) => new()
    {
        Name = name,
        Annotations = new ToolAnnotations { ReadOnlyHint = readOnly }
    };
}
