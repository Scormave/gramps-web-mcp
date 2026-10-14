using System.Text;
using System.Text.Json;
using GrampsWeb.Mcp.Formatters;
using GrampsWeb.Mcp.Input;
using GrampsWeb.Mcp.Models;
using GrampsWeb.Mcp.Serialization;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

/// <summary>Image regions on media links: reading them from tool arguments and showing stored ones.</summary>
public class MediaRegionTests
{
    [Fact]
    public void TryCreate_Rounds_To_Whole_Percent()
    {
        Assert.True(MediaRegion.TryCreate([10.4, 19.5, 60, 45.49], out var region, out _));

        Assert.Equal(new MediaRegion(10, 20, 60, 45), region);
        Assert.Equal((50, 25), (region.Width, region.Height));
        Assert.Equal("[10, 20, 60, 45]", region.ToString());
    }

    [Theory]
    [InlineData(new[] { 10.0, 20, 60 }, "needs 4 numbers")]
    [InlineData(new[] { -1.0, 20, 60, 45 }, "-1 is outside that")]
    [InlineData(new[] { 10.0, 20, 100.5, 45 }, "100.5 is outside that")]
    [InlineData(new[] { 60.0, 20, 10, 45 }, "is empty")]
    [InlineData(new[] { 10.0, 20, 10.4, 45 }, "is empty")]
    public void TryCreate_Rejects_Regions_Outside_The_Image_Or_Empty(double[] edges, string message)
    {
        Assert.False(MediaRegion.TryCreate(edges, out var region, out var error));

        Assert.Null(region);
        Assert.Contains(message, error);
    }

    [Theory]
    [InlineData("[10, 20, 60, 45]")]
    [InlineData("[\"10\", \"20%\", 60, 45]")]
    [InlineData("\"10,20,60,45\"")]
    [InlineData("\"[10; 20; 60; 45]\"")]
    [InlineData("\"10% 20% 60% 45%\"")]
    public void TryRead_Accepts_Arrays_And_Strings(string json)
    {
        using var doc = JsonDocument.Parse(json);

        Assert.True(MediaRegion.TryRead(doc.RootElement, out var region, out _));
        Assert.Equal(new MediaRegion(10, 20, 60, 45), region);
    }

    [Theory]
    [InlineData("{\"left\": 10}", "is an array")]
    [InlineData("[10, true, 60, 45]", "must be numbers")]
    [InlineData("\"left half\"", "must be numbers")]
    public void TryRead_Explains_Unreadable_Regions(string json, string message)
    {
        using var doc = JsonDocument.Parse(json);

        Assert.False(MediaRegion.TryRead(doc.RootElement, out _, out var error));
        Assert.Contains(message, error);
    }

    [Fact]
    public void TryRead_Null_Is_No_Region()
    {
        using var doc = JsonDocument.Parse("null");

        Assert.True(MediaRegion.TryRead(doc.RootElement, out var region, out var error));
        Assert.Null(region);
        Assert.Null(error);
    }

    [Fact]
    public void FromStored_Ignores_The_Whole_Image_And_Malformed_Rects()
    {
        Assert.Equal(new MediaRegion(10, 20, 60, 45), MediaRegion.FromStored([10, 20, 60, 45]));
        Assert.Null(MediaRegion.FromStored(null));
        Assert.Null(MediaRegion.FromStored([0, 0, 100, 100]));
        Assert.Null(MediaRegion.FromStored([10, 20, 60]));
        Assert.Null(MediaRegion.FromStored([60, 20, 10, 45]));
    }

    [Fact]
    public void Link_List_Reads_Handles_Objects_And_Rect_Alias()
    {
        var links = Deserialize("""["m1", {"ref": "m2", "region": [10, 20, 60, 45]}, {"handle": "m3", "Rect": "0,0,100,100"}, {"id": "m4"}, "", {"region": [1, 2, 3, 4]}]""");

        Assert.Equal(
            new[]
            {
                new MediaLink("m1"),
                new MediaLink("m2", new MediaRegion(10, 20, 60, 45)),
                new MediaLink("m3", MediaRegion.WholeImage),
                new MediaLink("m4"),
            },
            links.Items);
        Assert.True(links.Items[2].Region!.IsWholeImage);
    }

    [Theory]
    [InlineData("\"m1, m2\"")]
    [InlineData("\"[\\\"m1\\\", \\\"m2\\\"]\"")]
    public void Link_List_Reads_Strings_Like_A_Handle_List(string json)
    {
        Assert.Equal(new[] { new MediaLink("m1"), new MediaLink("m2") }, Deserialize(json).Items);
    }

    [Fact]
    public void Link_List_Keeps_An_Unreadable_Region_For_The_Tool_To_Report()
    {
        var links = Deserialize("""[{"ref": "m1", "region": [10, 20, 5, 45]}]""");

        var link = Assert.Single(links.Items);
        Assert.Null(link.Region);
        Assert.Contains("is empty", link.RegionError);
        var ex = Assert.Throws<ModelContextProtocol.McpException>(() => FlexibleMediaLinkList.EnsureValid(links, "mediaHandles"));
        Assert.Contains("mediaHandles: m1: region [10, 20, 5, 45] is empty", ex.Message);
        Assert.Contains("[left, top, right, bottom]", ex.Message);
    }

    [Fact]
    public void Link_List_Writes_Regions_As_Objects()
    {
        var links = new FlexibleMediaLinkList { Items = [new MediaLink("m1"), new MediaLink("m2", new MediaRegion(10, 20, 60, 45))] };

        Assert.Equal("""["m1",{"ref":"m2","region":[10,20,60,45]}]""", JsonSerializer.Serialize(links));
    }

    [Fact]
    public void Media_Sections_Show_The_Region_A_Link_Shows()
    {
        var links = JsonSerializer.Deserialize<GrampsMediaRef[]>(
            """[{"ref":"m1","rect":[10,20,60,45]},{"ref":"m1","rect":[0,0,100,100]},{"ref":"m2"}]""", GrampsJson.Options);
        var sb = new StringBuilder();

        MediaFormatter.AppendMediaLinkSection(sb, "Media", links, new Dictionary<string, string> { ["m1"] = "Page 12" });

        Assert.Equal(
            "\nMedia (3):\n" +
            "  • Page 12 [handle: m1] region [10, 20, 60, 45]\n" +
            "  • Page 12 [handle: m1]\n" +
            "  • [handle: m2]\n",
            sb.ToString().Replace("\r\n", "\n"));
    }

    [Fact]
    public void Gallery_Matches_Resolved_Media_To_Links_By_Position()
    {
        var links = JsonSerializer.Deserialize<GrampsMediaRef[]>(
            """[{"ref":"m1"},{"ref":"m1","rect":[5,50,95,75]}]""", GrampsJson.Options);
        GrampsMedia[] media = [new() { Handle = "m1", Description = "Page 12" }, new() { Handle = "m1", Description = "Page 12" }];
        var sb = new StringBuilder();

        MediaFormatter.AppendExtendedMediaSection(sb, media, links);

        Assert.Equal(
            "\nGallery (media) (2):\n" +
            "  • Page 12 [handle: m1]\n" +
            "  • Page 12 [handle: m1] region [5, 50, 95, 75]\n",
            sb.ToString().Replace("\r\n", "\n"));
    }

    private static FlexibleMediaLinkList Deserialize(string json) => JsonSerializer.Deserialize<FlexibleMediaLinkList>(json)!;
}
