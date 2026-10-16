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

        Assert.Equal(31, toolMethods.Count);
        Assert.Single(toolMethods, method => method.Name == "ReadMedia");
        Assert.Single(toolMethods, method => method.Name == "CreateMedia");
        Assert.DoesNotContain(toolMethods, method => method.Name is "GetMediaThumbnail" or "GetMediaFile");
        Assert.Single(toolMethods, method => method.Name == "DeleteObject");
        Assert.Single(toolMethods, method => method.Name == "GetObject");
        Assert.Single(toolMethods, method => method.Name == "GetReference");
        Assert.Single(toolMethods, method => method.Name == "GetPersonTree");
        Assert.Single(toolMethods, method => method.Name == "GetTimeline");
        Assert.DoesNotContain(toolMethods, method => method.Name.StartsWith("DeletePerson", StringComparison.Ordinal));
        Assert.DoesNotContain(toolMethods, method => method.Name == "GetPerson");
        Assert.DoesNotContain(toolMethods, method => method.Name is "GetAncestors" or "GetDescendants");
        Assert.DoesNotContain(toolMethods, method => method.Name is "GetPersonTimeline" or "GetFamilyTimeline" or "GetPlaceTimeline");
        Assert.DoesNotContain(toolMethods, method => method.Name == "FindByGrampsId");
        Assert.DoesNotContain(toolMethods, method => method.Name is "GetInputGuide" or "GetTypes" or "GetMetadata" or "GetNameSettings");
    }

    /// <summary>
    /// Every tool works on the one configured tree, so none is open-world. Creates only add, while updates
    /// overwrite fields and replace mode removes links, so updates and delete are destructive. A write tool also
    /// states whether repeating the same call changes anything more: updates and delete settle on the same state
    /// (add mode skips handles already linked, an identical upload returns the stored media), while plain creates
    /// add another record and update_note can append the same text again.
    /// </summary>
    [Fact]
    public void Tools_Expose_Destructive_Idempotent_And_Closed_World_Hints()
    {
        string[] idempotent =
        [
            "create_media", "update_citation", "update_event", "update_family", "update_media", "update_person",
            "update_place", "update_repository", "update_source", "update_tag", "delete_object"
        ];
        string[] repeatable =
        [
            "create_citation", "create_event", "create_family", "create_note", "create_person", "create_place",
            "create_repository", "create_source", "create_tag", "update_note"
        ];

        var tools = typeof(PersonTools).Assembly
            .GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .Where(m => m.GetCustomAttribute<McpServerToolAttribute>() is not null)
            .Select(m => McpServerTool.Create(m).ProtocolTool)
            .ToList();

        foreach (var tool in tools)
        {
            var annotations = tool.Annotations;
            Assert.NotNull(annotations);
            Assert.False(annotations.OpenWorldHint ?? true, $"Tool {tool.Name} must be closed-world");
            if (annotations.ReadOnlyHint == true)
            {
                Assert.Null(annotations.IdempotentHint);
                continue;
            }

            Assert.Equal(idempotent.Contains(tool.Name), annotations.IdempotentHint);
            Assert.Equal(!tool.Name.StartsWith("create_", StringComparison.Ordinal), annotations.DestructiveHint);
        }

        Assert.Equal(
            idempotent.Concat(repeatable).Order(StringComparer.Ordinal),
            tools.Where(tool => tool.Annotations?.ReadOnlyHint != true).Select(tool => tool.Name).Order(StringComparer.Ordinal));
    }
}
