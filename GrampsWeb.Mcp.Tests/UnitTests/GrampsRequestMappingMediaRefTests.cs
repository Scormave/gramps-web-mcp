using System.Text.Json;
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
        Assert.Null(GrampsRequestMapping.ToMediaRefRequests(null));
        Assert.Null(GrampsRequestMapping.ToMediaRefRequests([]));
    }
}
