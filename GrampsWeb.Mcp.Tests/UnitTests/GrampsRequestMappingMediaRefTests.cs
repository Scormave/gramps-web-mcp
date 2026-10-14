using System.Text.Json;
using GrampsWeb.Mcp.Input;
using GrampsWeb.Mcp.Models;
using GrampsWeb.Mcp.Requests;
using GrampsWeb.Mcp.Serialization;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

public class GrampsRequestMappingMediaRefTests
{
    [Fact]
    public void ToMediaRefRequests_Serializes_As_MediaRef_Objects_With_Class()
    {
        var arr = GrampsRequestMapping.ToMediaRefRequests([new MediaLink("h1"), new MediaLink("h2")]);
        Assert.NotNull(arr);
        Assert.Equal(2, arr!.Length);

        var json = JsonSerializer.Serialize(new CreatePersonRequest
        {
            Class = "Person",
            MediaList = arr
        }, GrampsJson.Options);

        Assert.Contains("\"media_list\"", json);
        Assert.Contains("\"_class\":\"MediaRef\"", json);
        Assert.Contains("\"ref\":\"h1\"", json);
        Assert.Contains("\"ref\":\"h2\"", json);
        Assert.DoesNotContain("rect", json);
    }

    [Fact]
    public void ToMediaRefRequests_Sends_A_Region_As_Rect_And_The_Whole_Image_As_None()
    {
        var arr = GrampsRequestMapping.ToMediaRefRequests(
            [new MediaLink(" h1 ", new MediaRegion(10, 20, 60, 45)), new MediaLink("h2", MediaRegion.WholeImage)]);

        Assert.Equal("h1", arr![0].Ref);
        Assert.Equal(new[] { 10, 20, 60, 45 }, arr[0].Rect);
        Assert.Null(arr[1].Rect);
    }

    [Fact]
    public void ToMediaRefRequests_Rejects_An_Unreadable_Region()
    {
        var ex = Assert.Throws<ModelContextProtocol.McpException>(() =>
            GrampsRequestMapping.ToMediaRefRequests([new MediaLink("h1", RegionError: "a region needs 4 numbers [left, top, right, bottom], not 3.")]));

        Assert.Contains("mediaHandles: h1: a region needs 4 numbers", ex.Message);
    }

    [Fact]
    public void ToMediaRefRequests_Null_Or_Empty_Returns_Null()
    {
        Assert.Null(GrampsRequestMapping.ToMediaRefRequests(null));
        Assert.Null(GrampsRequestMapping.ToMediaRefRequests([]));
    }
}
