using System.Text;
using System.Text.Json;
using GrampsWeb.Mcp.Formatters;
using GrampsWeb.Mcp.Models;
using GrampsWeb.Mcp.Tools;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

public class BacklinkCollectorTests
{
    [Fact]
    public void ParseGroupsFromResponseRoot_MergesAliasesAndSorts()
    {
        using var doc = JsonDocument.Parse(
            """
            {
              "handle": "c1",
              "backlinks": {
                "person": ["zebra", {"ref": "alpha"}],
                "people": [{"handle": "mule"}],
                "event": ["beta"]
              }
            }
            """);

        var groups = BacklinkCollector.ParseGroupsFromResponseRoot(doc.RootElement);

        Assert.Equal(2, groups.Count);
        Assert.Equal("people", groups[0].Key);
        Assert.Equal(new[] { "alpha", "mule", "zebra" }, groups[0].Handles);
        Assert.Equal("events", groups[1].Key);
        Assert.Equal(new[] { "beta" }, groups[1].Handles);
    }

    [Fact]
    public void ParseGroupsFromResponseRoot_ReturnsEmpty_WhenNoBacklinksObject()
    {
        using var doc = JsonDocument.Parse("""{"handle":"x"}""");
        Assert.Empty(BacklinkCollector.ParseGroupsFromResponseRoot(doc.RootElement));
    }

    [Fact]
    public void ParseGroupsFromResponseRoot_IgnoresUnknownKeys()
    {
        using var doc = JsonDocument.Parse(
            """
            {"backlinks":{"tag":["t1"],"citation":["c1"]}}
            """);

        var groups = BacklinkCollector.ParseGroupsFromResponseRoot(doc.RootElement);
        Assert.Single(groups);
        Assert.Equal("citations", groups[0].Key);
    }

    [Fact]
    public void BacklinkFormatter_AppendsReferencedBySections()
    {
        var sb = new StringBuilder();
        sb.AppendLine("HEAD");
        var groups = new List<BacklinkGroup>
        {
            new("events", "events", new[] { "e2", "e1" })
        };
        BacklinkFormatter.AppendReferencedBySections(sb, groups);

        var text = sb.ToString();
        Assert.Contains("Referenced by events (2) [READ-ONLY", text);
        Assert.Contains("  • event [handle: e2]", text);
        Assert.Contains("  • event [handle: e1]", text);
    }
}
