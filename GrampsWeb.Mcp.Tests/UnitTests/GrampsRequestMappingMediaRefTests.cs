using System.Text.Json;
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
        var arr = GrampsRequestMapping.ToMediaRefRequests(["h1", "h2"]);
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
    }

    [Fact]
    public void ToMediaRefRequests_Null_Or_Empty_Returns_Null()
    {
        Assert.Null(GrampsRequestMapping.ToMediaRefRequests((string[]?)null));
        Assert.Null(GrampsRequestMapping.ToMediaRefRequests(Array.Empty<string>()));
        Assert.Null(GrampsRequestMapping.ToMediaRefRequests((GrampsMediaRef[]?)null));
        Assert.Null(GrampsRequestMapping.ToMediaRefRequests(Array.Empty<GrampsMediaRef>()));
    }

    [Fact]
    public void ToMediaRefRequests_From_GrampsMediaRef_Preserves_Rect()
    {
        var arr = GrampsRequestMapping.ToMediaRefRequests(new[]
        {
            new GrampsMediaRef { Ref = "m1", Rect = [10, 20, 100, 200], Private = true }
        });
        Assert.NotNull(arr);
        Assert.Single(arr!);
        Assert.Equal("m1", arr[0].Ref);
        Assert.Equal(new[] { 10, 20, 100, 200 }, arr[0].Rect);
        Assert.True(arr[0].Private);
    }

    [Fact]
    public void ToMediaRefRequests_Merge_Preserves_Rect_When_Handle_List_Overlaps()
    {
        var existing = new[]
        {
            new GrampsMediaRef { Ref = "keep", Rect = [1, 2, 3, 4] },
            new GrampsMediaRef { Ref = "drop", Rect = [9, 9, 9, 9] }
        };
        var arr = GrampsRequestMapping.ToMediaRefRequests(["keep", "newref"], existing);
        Assert.NotNull(arr);
        Assert.Equal(2, arr!.Length);
        Assert.Equal("keep", arr[0].Ref);
        Assert.Equal(new[] { 1, 2, 3, 4 }, arr[0].Rect);
        Assert.Null(arr[1].Rect);
        Assert.Equal("newref", arr[1].Ref);
    }
}
