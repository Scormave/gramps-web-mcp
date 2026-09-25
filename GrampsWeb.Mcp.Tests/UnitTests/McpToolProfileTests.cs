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
                Tool("get_media_thumbnail", readOnly: true),
                Tool("get_media_file", readOnly: true),
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
                Tool("get_media_thumbnail", readOnly: true),
                Tool("create_person", readOnly: false)
            ]
        };

        var filtered = McpToolProfileExtensions.KeepAvailableTools(result, Config(readOnly: true, mediaResourcesEnabled: false));

        Assert.Equal(["get_object"], filtered.Tools.Select(tool => tool.Name));
    }

    private static GrampsWeb.Mcp.Config.GrampsConfig Config(bool readOnly, bool mediaResourcesEnabled) => new(
        ApiUrl: "https://gramps-web.test",
        Username: "user",
        Password: "pass",
        TreeId: "tree",
        ReadOnly: readOnly,
        MediaResourcesEnabled: mediaResourcesEnabled);

    private static Tool Tool(string name, bool? readOnly) => new()
    {
        Name = name,
        Annotations = new ToolAnnotations { ReadOnlyHint = readOnly }
    };
}
