using ModelContextProtocol.Protocol;
using Xunit;

namespace GrampsWeb.Mcp.IntegrationTests;

/// <summary>Resources and prompts over the MCP protocol.</summary>
[Collection(GrampsWebCollection.Name)]
public sealed class ProtocolTests(GrampsWebFixture gramps)
{
    private McpSession Session => gramps.Session;

    [IntegrationFact]
    public async Task MetadataResource_DescribesTheTree()
    {
        var resource = await Session.Client.ReadResourceAsync("gramps://metadata");

        var text = Assert.IsType<TextResourceContents>(Assert.Single(resource.Contents)).Text;
        Assert.Contains("Integration tests", text);
        Assert.Contains(gramps.TreeId, text);
    }

    [IntegrationFact]
    public async Task TypesResource_ListsEventTypes()
    {
        var resource = await Session.Client.ReadResourceAsync("gramps://types");

        var text = Assert.IsType<TextResourceContents>(Assert.Single(resource.Contents)).Text;
        Assert.Contains("Birth", text);
        Assert.Contains("Marriage", text);
    }

    [IntegrationFact]
    public async Task Prompts_AreListedAndFilledIn()
    {
        var prompts = (await Session.Client.ListPromptsAsync()).Select(prompt => prompt.Name).ToArray();
        Assert.Contains("add-person", prompts);
        Assert.Contains("cite-fact", prompts);

        var prompt = await Session.Client.GetPromptAsync("add-person", new Dictionary<string, object?>
        {
            ["name"] = "Ivan Petrov",
            ["birthDate"] = "about 1880"
        });

        var text = Assert.IsType<TextContentBlock>(Assert.Single(prompt.Messages).Content).Text;
        Assert.Contains("Name: Ivan Petrov", text);
        Assert.Contains("Birth: about 1880", text);
    }
}
