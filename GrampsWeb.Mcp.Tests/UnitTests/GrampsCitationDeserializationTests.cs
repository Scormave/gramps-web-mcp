using System.Text.Json;
using GrampsWeb.Mcp.Models;
using GrampsWeb.Mcp.Serialization;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

public class GrampsCitationDeserializationTests
{
    [Fact]
    public void MediaList_As_MediaReference_Objects_Deserializes_Full_MediaRef()
    {
        const string json = """
            {
              "handle": "c1",
              "gramps_id": "C0001",
              "source_handle": "s1",
              "media_list": [
                { "ref": "mhandle1", "private": false },
                { "ref": "mhandle2" }
              ]
            }
            """;
        var c = JsonSerializer.Deserialize<GrampsCitation>(json, GrampsJson.Options);
        Assert.NotNull(c);
        Assert.NotNull(c!.MediaList);
        Assert.Equal(2, c.MediaList!.Length);
        Assert.Equal("mhandle1", c.MediaList[0].ResolvedRef);
        Assert.Equal("mhandle2", c.MediaList[1].ResolvedRef);
    }

    [Fact]
    public void MediaList_As_Plain_String_Handles_Still_Works()
    {
        const string json = """{"handle":"c1","media_list":["a","b"]}""";
        var c = JsonSerializer.Deserialize<GrampsCitation>(json, GrampsJson.Options);
        Assert.NotNull(c!.MediaList);
        Assert.Equal(2, c.MediaList!.Length);
        Assert.Equal("a", c.MediaList[0].ResolvedRef);
        Assert.Equal("b", c.MediaList[1].ResolvedRef);
    }
}
