using GrampsWeb.Mcp.Hosting;
using ModelContextProtocol.Protocol;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

public class McpToolProfileTests
{
    [Fact]
    public void KeepReadOnlyTools_RemovesWriteToolsFromPublishedCatalog()
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

        var filtered = McpToolProfileExtensions.KeepReadOnlyTools(result);

        Assert.Same(result, filtered);
        var tool = Assert.Single(filtered.Tools);
        Assert.Equal("get_object", tool.Name);
    }

    private static Tool Tool(string name, bool? readOnly) => new()
    {
        Name = name,
        Annotations = new ToolAnnotations { ReadOnlyHint = readOnly }
    };
}
