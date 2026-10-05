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
        Assert.Contains("do not check for duplicates", text);
        Assert.DoesNotContain("read_media", text);
        Assert.DoesNotContain("read-only: no tool", text);
    }

    [Fact]
    public void For_ReadOnlyServer_LeavesOutWriteGuidance()
    {
        var text = GrampsServerInstructions.For(Config(readOnly: true, mediaResourcesEnabled: false));

        Assert.Contains("This server is read-only", text);
        Assert.DoesNotContain("Changing the tree:", text);
        Assert.DoesNotContain("delete_object", text);
    }

    [Fact]
    public void For_MediaEnabled_MentionsReadMedia()
    {
        var text = GrampsServerInstructions.For(Config(readOnly: true, mediaResourcesEnabled: true));

        Assert.Contains("read_media", text);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void For_NamesOnlyToolsTheServerPublishes(bool readOnly, bool mediaResourcesEnabled)
    {
        var text = GrampsServerInstructions.For(Config(readOnly, mediaResourcesEnabled));
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
            var hidden = (readOnly && !toolReadOnly) || (!mediaResourcesEnabled && name == "read_media");
            if (hidden)
                Assert.DoesNotContain(name, text);
        }
        foreach (var named in new[] { "search", "list_objects", "get_object", "get_person_tree", "get_relations", "get_timeline" })
            Assert.Contains(published, tool => tool.Name == named);
    }

    private static string ToSnakeCase(string name) =>
        string.Concat(name.Select((c, i) => char.IsUpper(c) && i > 0 ? "_" + char.ToLowerInvariant(c) : char.ToLowerInvariant(c).ToString()));

    private static GrampsConfig Config(bool readOnly, bool mediaResourcesEnabled) => new(
        ApiUrl: "https://gramps-web.test",
        Username: "user",
        Password: "pass",
        TreeId: "tree",
        ReadOnly: readOnly,
        MediaResourcesEnabled: mediaResourcesEnabled);
}
