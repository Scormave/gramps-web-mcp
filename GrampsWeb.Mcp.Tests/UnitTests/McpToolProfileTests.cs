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
                Tool("get_object", readOnly: true),
                Tool("create_person", readOnly: false),
                Tool("unannotated", readOnly: null)
            ]
        };

        var filtered = McpToolProfileExtensions.KeepAvailableTools(result, Config(readOnly: true, mediaResourcesEnabled: true));

        Assert.Same(result, filtered);
        var tool = Assert.Single(filtered.Tools);
        Assert.Equal("get_object", tool.Name);
    }

    [Fact]
    public void KeepAvailableTools_RemovesMediaByteToolsWhenDisabled()
    {
        var result = new ListToolsResult
        {
            Tools =
            [
                Tool("get_object", readOnly: true),
                Tool("read_media", readOnly: true),
                Tool("update_media", readOnly: false)
            ]
        };

        var filtered = McpToolProfileExtensions.KeepAvailableTools(result, Config(readOnly: false, mediaResourcesEnabled: false));

        Assert.Equal(["get_object", "update_media"], filtered.Tools.Select(tool => tool.Name));
    }

    [Fact]
    public void KeepAvailableTools_AppliesReadOnlyAndMediaFiltersTogether()
    {
        var result = new ListToolsResult
        {
            Tools =
            [
                Tool("get_object", readOnly: true),
                Tool("read_media", readOnly: true),
                Tool("create_person", readOnly: false)
            ]
        };

        var filtered = McpToolProfileExtensions.KeepAvailableTools(result, Config(readOnly: true, mediaResourcesEnabled: false));

        Assert.Equal(["get_object"], filtered.Tools.Select(tool => tool.Name));
    }

    [Theory]
    [InlineData(false, new[] { "get_object", "update_media" })]
    [InlineData(true, new[] { "get_object", "create_media", "update_media" })]
    public void KeepAvailableTools_PublishesCreateMediaOnlyWithUploadFolders(bool uploadEnabled, string[] expected)
    {
        var result = new ListToolsResult
        {
            Tools =
            [
                Tool("get_object", readOnly: true),
                Tool("create_media", readOnly: false),
                Tool("update_media", readOnly: false)
            ]
        };

        var filtered = McpToolProfileExtensions.KeepAvailableTools(result,
            Config(readOnly: false, mediaResourcesEnabled: true, mediaUploadEnabled: uploadEnabled));

        Assert.Equal(expected, filtered.Tools.Select(tool => tool.Name));
    }

    [Fact]
    public void KeepAvailableTools_ReadOnlyModeHidesCreateMediaEvenWithUploadFolders()
    {
        var result = new ListToolsResult { Tools = [Tool("get_object", readOnly: true), Tool("create_media", readOnly: false)] };

        var filtered = McpToolProfileExtensions.KeepAvailableTools(result,
            Config(readOnly: true, mediaResourcesEnabled: true, mediaUploadEnabled: true));

        Assert.Equal(["get_object"], filtered.Tools.Select(tool => tool.Name));
    }

    private static GrampsWeb.Mcp.Config.GrampsConfig Config(bool readOnly, bool mediaResourcesEnabled, bool mediaUploadEnabled = false) => new(
        ApiUrl: "https://gramps-web.test",
        Username: "user",
        Password: "pass",
        TreeId: "tree",
        ReadOnly: readOnly,
        MediaResourcesEnabled: mediaResourcesEnabled,
        MediaUploadDirs: mediaUploadEnabled ? ["/srv/uploads"] : null);

    private static Tool Tool(string name, bool? readOnly) => new()
    {
        Name = name,
        Annotations = new ToolAnnotations { ReadOnlyHint = readOnly }
    };
}
