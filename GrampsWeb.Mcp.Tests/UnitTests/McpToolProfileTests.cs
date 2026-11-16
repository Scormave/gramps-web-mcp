using System.Text.Json;
using GrampsWeb.Mcp.Hosting;
using GrampsWeb.Mcp.Tools;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
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
    public void KeepAvailableTools_PublishesCreateMediaOnlyWithUploadFolders(bool uploadFolders, string[] expected)
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
            Config(readOnly: false, mediaResourcesEnabled: true, uploadFolders: uploadFolders));

        Assert.Equal(expected, filtered.Tools.Select(tool => tool.Name));
    }

    [Fact]
    public void KeepAvailableTools_ReadOnlyModeHidesCreateMediaEvenWithUploadFolders()
    {
        var result = new ListToolsResult { Tools = [Tool("get_object", readOnly: true), Tool("create_media", readOnly: false)] };

        var filtered = McpToolProfileExtensions.KeepAvailableTools(result,
            Config(readOnly: true, mediaResourcesEnabled: true, uploadFolders: true));

        Assert.Equal(["get_object"], filtered.Tools.Select(tool => tool.Name));
    }

    [Fact]
    public void KeepAvailableTools_DisabledMediaAccessHidesCreateMediaEvenWithUploadFolders()
    {
        var result = new ListToolsResult
        {
            Tools =
            [
                Tool("get_object", readOnly: true),
                Tool("read_media", readOnly: true),
                Tool("create_media", readOnly: false),
                Tool("update_media", readOnly: false)
            ]
        };

        var filtered = McpToolProfileExtensions.KeepAvailableTools(result,
            Config(readOnly: false, mediaResourcesEnabled: false, uploadFolders: true));

        Assert.Equal(["get_object", "update_media"], filtered.Tools.Select(tool => tool.Name));
    }

    [Fact]
    public void KeepAvailableTools_PublishesCreateMediaWithUploadHostsAlone()
    {
        var result = new ListToolsResult { Tools = [Tool("get_object", readOnly: true), Tool("create_media", readOnly: false)] };

        var filtered = McpToolProfileExtensions.KeepAvailableTools(result,
            Config(readOnly: false, mediaResourcesEnabled: true, uploadHosts: true));

        Assert.Equal(["get_object", "create_media"], filtered.Tools.Select(tool => tool.Name));
    }

    [Theory]
    [InlineData(nameof(MediaTools.CreateMedia))]
    [InlineData(nameof(MediaTools.UpdateMedia))]
    public void UploadTools_NameTheirFileArgumentForChatGpt(string method)
    {
        var tool = MediaTool(method);

        Assert.Equal("""["file"]""", tool.Meta!["openai/fileParams"]!.ToJsonString());
        var file = tool.InputSchema.GetProperty("properties").GetProperty("file");
        Assert.Equal(["download_url", "file_id", "file_name", "mime_type"],
            file.GetProperty("properties").EnumerateObject().Select(property => property.Name).Order());
        Assert.Equal(["download_url", "file_id"], file.GetProperty("required").EnumerateArray().Select(name => name.GetString()));
    }

    [Theory]
    [InlineData(true, true, new[] { "filePath", "file" }, null)]
    [InlineData(true, false, new[] { "filePath" }, "filePath")]
    [InlineData(false, true, new[] { "file" }, "file")]
    public void KeepAvailableTools_ListsOnlyTheUploadSourcesTheServerAllows(bool uploadFolders, bool uploadHosts, string[] sources, string? uploadRequires)
    {
        var upload = MediaTool(nameof(MediaTools.CreateMedia));
        var update = MediaTool(nameof(MediaTools.UpdateMedia));
        var result = new ListToolsResult { Tools = [Tool("get_object", readOnly: true), upload, update] };

        var filtered = McpToolProfileExtensions.KeepAvailableTools(result,
            Config(readOnly: false, mediaResourcesEnabled: true, uploadFolders: uploadFolders, uploadHosts: uploadHosts));

        Assert.Equal(["get_object", "create_media", "update_media"], filtered.Tools.Select(tool => tool.Name));
        foreach (var tool in filtered.Tools.Skip(1))
        {
            var properties = tool.InputSchema.GetProperty("properties");
            Assert.Equal(sources, new[] { "filePath", "file" }.Where(name => properties.TryGetProperty(name, out _)));
            var required = tool.InputSchema.TryGetProperty("required", out var names)
                ? names.EnumerateArray().Select(name => name.GetString()).ToList()
                : [];
            if (tool.Name == "create_media")
                Assert.Equal(uploadRequires is null ? [] : [uploadRequires], required);
            else
                Assert.Equal(["handle"], required);

            Assert.Equal(uploadHosts, tool.Meta?["openai/fileParams"] is not null);
            if (!uploadHosts)
                continue;
            var file = properties.GetProperty("file");
            Assert.Equal("object", file.GetProperty("type").GetString());
            Assert.False(file.TryGetProperty("default", out _));
            // The OpenAI file schema types every field as a plain string; the optional ones stay optional.
            foreach (var field in file.GetProperty("properties").EnumerateObject())
                Assert.Equal("string", field.Value.GetProperty("type").GetString());
            Assert.Equal(["download_url", "file_id"], file.GetProperty("required").EnumerateArray().Select(name => name.GetString()));
        }

        // Calls are checked against the registered tools, so those keep every argument.
        foreach (var tool in new[] { upload, update })
        {
            Assert.True(tool.InputSchema.GetProperty("properties").TryGetProperty("filePath", out _));
            Assert.True(tool.InputSchema.GetProperty("properties").TryGetProperty("file", out _));
            Assert.NotNull(tool.Meta?["openai/fileParams"]);
        }
    }

    private static Tool MediaTool(string method) =>
        McpServerTool.Create(typeof(MediaTools).GetMethod(method)!).ProtocolTool;

    private static GrampsWeb.Mcp.Config.GrampsConfig Config(bool readOnly, bool mediaResourcesEnabled, bool uploadFolders = false,
        bool uploadHosts = false) => new(
        ApiUrl: "https://gramps-web.test",
        Username: "user",
        Password: "pass",
        TreeId: "tree",
        ReadOnly: readOnly,
        MediaResourcesEnabled: mediaResourcesEnabled,
        MediaUploadDirs: uploadFolders ? ["/srv/uploads"] : null,
        MediaUploadHosts: uploadHosts ? ["files.example.com"] : null);

    private static Tool Tool(string name, bool? readOnly) => new()
    {
        Name = name,
        Annotations = new ToolAnnotations { ReadOnlyHint = readOnly }
    };
}
