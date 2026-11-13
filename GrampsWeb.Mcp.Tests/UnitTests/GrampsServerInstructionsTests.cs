using GrampsWeb.Mcp.Config;
using GrampsWeb.Mcp.Hosting;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

public class GrampsServerInstructionsTests
{
    [Fact]
    public void For_WritableServer_ExplainsReadingAndWriting()
    {
        var text = GrampsServerInstructions.For(Config(readOnly: false, mediaResourcesEnabled: false));

        Assert.Contains("start with search", text);
        Assert.Contains("Changing the tree:", text);
        Assert.Contains("check for duplicates", text);
        Assert.DoesNotContain("get_media_image", text);
        Assert.DoesNotContain("read-only: no tool", text);
    }

    [Fact]
    public void For_ReadOnlyServer_LeavesOutWriteGuidance()
    {
        var text = GrampsServerInstructions.For(Config(readOnly: true, mediaResourcesEnabled: false));

        Assert.Contains("This server is read-only", text);
        Assert.Contains("find_duplicates", text);
        Assert.DoesNotContain("Changing the tree:", text);
        Assert.DoesNotContain("delete_records", text);
    }

    [Fact]
    public void For_MediaEnabled_MentionsGetMediaImage()
    {
        var text = GrampsServerInstructions.For(Config(readOnly: true, mediaResourcesEnabled: true));

        Assert.Contains("get_media_image", text);
    }

    [Fact]
    public void For_MentionsUploadMediaOnlyWhenUploadingIsOn()
    {
        Assert.Contains("upload_media", GrampsServerInstructions.For(Config(readOnly: false, mediaResourcesEnabled: true, uploadFolders: true)));
        Assert.DoesNotContain("upload_media", GrampsServerInstructions.For(Config(readOnly: true, mediaResourcesEnabled: true, uploadFolders: true)));
        Assert.DoesNotContain("upload_media", GrampsServerInstructions.For(Config(readOnly: false, mediaResourcesEnabled: true, uploadFolders: false)));
        Assert.DoesNotContain("upload_media", GrampsServerInstructions.For(Config(readOnly: false, mediaResourcesEnabled: false, uploadFolders: true)));
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void For_NamesOnlyToolsTheServerPublishes(bool readOnly, bool mediaResourcesEnabled, bool uploadFolders)
    {
        var text = GrampsServerInstructions.For(Config(readOnly, mediaResourcesEnabled, uploadFolders));
        var published = typeof(GrampsServerInstructions).Assembly.GetTypes()
            .SelectMany(type => type.GetMethods())
            .Select(method => method.GetCustomAttributes(typeof(ModelContextProtocol.Server.McpServerToolAttribute), false)
                .Cast<ModelContextProtocol.Server.McpServerToolAttribute>().SingleOrDefault() is { } tool
                ? (Name: ToSnakeCase(method.Name), tool.ReadOnly)
                : default)
            .Where(tool => tool.Name != null)
            .ToList();

        foreach (var (name, toolReadOnly) in published)
        {
            var hidden = (readOnly && !toolReadOnly)
                         || (!mediaResourcesEnabled && name == "get_media_image")
                         || (!(mediaResourcesEnabled && uploadFolders) && name == "upload_media");
            if (hidden)
                Assert.DoesNotContain(name, text);
        }
        foreach (var named in new[] { "search", "list_records", "get_records", "get_person_tree", "get_relations", "get_timeline" })
            Assert.Contains(published, tool => tool.Name == named);
    }

    private static string ToSnakeCase(string name) =>
        string.Concat(name.Select((c, i) => char.IsUpper(c) && i > 0 ? "_" + char.ToLowerInvariant(c) : char.ToLowerInvariant(c).ToString()));

    private static GrampsConfig Config(bool readOnly, bool mediaResourcesEnabled, bool uploadFolders = false) => new(
        ApiUrl: "https://gramps-web.test",
        Username: "user",
        Password: "pass",
        TreeId: "tree",
        ReadOnly: readOnly,
        MediaResourcesEnabled: mediaResourcesEnabled,
        MediaUploadDirs: uploadFolders ? ["/srv/uploads"] : null);
}
