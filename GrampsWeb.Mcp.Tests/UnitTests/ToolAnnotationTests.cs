using System.Reflection;
using GrampsWeb.Mcp.Tools;
using ModelContextProtocol.Server;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

public class ToolAnnotationTests
{
    [Fact]
    public void All_McpServerTools_Have_Title_And_Hint_Annotations()
    {
        var toolMethods = typeof(PersonTools).Assembly
            .GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .Where(m => m.GetCustomAttribute<McpServerToolAttribute>() is not null)
            .ToList();

        Assert.NotEmpty(toolMethods);

        foreach (var method in toolMethods)
        {
            var attr = method.GetCustomAttribute<McpServerToolAttribute>()!;
            Assert.False(string.IsNullOrWhiteSpace(attr.Title), $"Tool {method.Name} is missing Title");

            if (attr.ReadOnly)
                Assert.False(attr.Destructive, $"Read-only tool {method.Name} must not be destructive");
        }

        Assert.Equal(57, toolMethods.Count);
    }
}
