using System.Text.Json;
using GrampsWeb.Mcp.Input;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

public class FlexibleHandleListTests
{
    private static FlexibleHandleList? Deserialize(string json)
        => JsonSerializer.Deserialize<FlexibleHandleList?>(json);

    [Fact]
    public void Deserializes_JsonArray_OfStrings()
    {
        var v = Deserialize("""["h1","h2"]""");
        Assert.NotNull(v);
        Assert.Equal(new[] { "h1", "h2" }, v!.Handles);
    }

    [Fact]
    public void Deserializes_JsonArray_OfRefObjects()
    {
        var v = Deserialize("""[{"ref":"a1"},{"handle":"b2"}]""");
        Assert.NotNull(v);
        Assert.Equal(new[] { "a1", "b2" }, v!.Handles);
    }

    [Fact]
    public void Deserializes_SinglePlainHandle_AsJsonString()
    {
        var v = Deserialize("\"GNUJQCL9MD64AM56OH\"");
        Assert.NotNull(v);
        Assert.Equal(new[] { "GNUJQCL9MD64AM56OH" }, v!.Handles);
    }

    [Fact]
    public void Deserializes_CommaSeparated_String()
    {
        var v = Deserialize("\"  a , b ; c \"");
        Assert.NotNull(v);
        Assert.Equal(new[] { "a", "b", "c" }, v!.Handles);
    }

    [Fact]
    public void Deserializes_JsonArrayString_AsSingleStringArgument()
    {
        var v = Deserialize("\"[\\\"x\\\",\\\"y\\\"]\"");
        Assert.NotNull(v);
        Assert.Equal(new[] { "x", "y" }, v!.Handles);
    }

    [Fact]
    public void Null_Json_Yields_Null()
    {
        Assert.Null(Deserialize("null"));
    }

    [Fact]
    public void Whitespace_String_Yields_Null()
    {
        Assert.Null(Deserialize("\"   \""));
    }

    [Fact]
    public void Empty_JsonArray_Yields_EmptyHandles()
    {
        var v = Deserialize("[]");
        Assert.NotNull(v);
        Assert.Empty(v!.Handles);
    }

    [Fact]
    public void Serializes_AsJsonArray()
    {
        var json = JsonSerializer.Serialize(new FlexibleHandleList { Handles = ["p", "q"] });
        Assert.Equal("""["p","q"]""", json);
    }
}
