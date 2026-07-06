using System.Text.Json;
using GrampsWeb.Mcp.Serialization;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

public class JsonElementPropertyReaderTests
{
    [Fact]
    public void GetString_Matches_Alias_Case_Insensitive()
    {
        using var doc = JsonDocument.Parse("""{"CallNumber":"A-1","REF":"h1"}""");
        var root = doc.RootElement;
        Assert.Equal("A-1", JsonElementPropertyReader.GetString(root, "call_number", "callNumber"));
        Assert.Equal("h1", JsonElementPropertyReader.GetString(root, "ref", "handle"));
    }

    [Fact]
    public void GetString_Returns_Null_When_Missing()
    {
        using var doc = JsonDocument.Parse("""{"ref":"h1"}""");
        Assert.Null(JsonElementPropertyReader.GetString(doc.RootElement, "call_number", "callNumber"));
    }

    [Fact]
    public void GetString_First_Alias_Wins_In_Property_Order()
    {
        using var doc = JsonDocument.Parse("""{"callNumber":"camel","call_number":"snake"}""");
        var value = JsonElementPropertyReader.GetString(doc.RootElement, "call_number", "callNumber");
        Assert.True(value is "camel" or "snake");
    }

    [Fact]
    public void GetBool_Reads_Boolean_Property()
    {
        using var doc = JsonDocument.Parse("""{"Private":true}""");
        Assert.True(JsonElementPropertyReader.GetBool(doc.RootElement, "private"));
    }

    [Fact]
    public void GetStringArray_Reads_CamelCase_Array()
    {
        using var doc = JsonDocument.Parse("""{"noteList":["n1","n2"]}""");
        Assert.Equal(new[] { "n1", "n2" }, JsonElementPropertyReader.GetStringArray(doc.RootElement, "note_list", "noteList"));
    }
}
