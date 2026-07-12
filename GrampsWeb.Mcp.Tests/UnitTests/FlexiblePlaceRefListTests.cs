using System.Text.Json;
using GrampsWeb.Mcp.Input;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

public class FlexiblePlaceRefListTests
{
    private static FlexiblePlaceRefList? Deserialize(string json)
        => JsonSerializer.Deserialize<FlexiblePlaceRefList?>(json);

    [Fact]
    public void Null_Yields_Null() => Assert.Null(Deserialize("null"));

    [Fact]
    public void Single_Handle_String_Parses()
    {
        var v = Deserialize("\"h1\"");
        Assert.NotNull(v);
        Assert.Single(v!.Items);
        Assert.Equal("h1", v.Items[0].Ref);
        Assert.Null(v.Items[0].Date);
    }

    [Fact]
    public void Handle_With_Date_Parses()
    {
        var v = Deserialize("\"h1::1920-1950\"");
        Assert.NotNull(v);
        Assert.Single(v!.Items);
        Assert.Equal("h1", v.Items[0].Ref);
        Assert.NotNull(v.Items[0].Date);
        Assert.Equal(1920, v.Items[0].Date!.Year);
        Assert.Equal(1950, v.Items[0].Date!.EndYear);
        Assert.Equal(5, v.Items[0].Date!.Modifier);
    }

    [Fact]
    public void Json_Object_Array_With_Date_Parses()
    {
        var v = Deserialize("""[{"ref":"h1","date":"1920"}]""");
        Assert.NotNull(v);
        Assert.Single(v!.Items);
        Assert.Equal("h1", v.Items[0].Ref);
        Assert.Equal(1920, v.Items[0].Date!.Year);
    }

    [Fact]
    public void Empty_Array_Yields_Empty_Items()
    {
        var v = Deserialize("[]");
        Assert.NotNull(v);
        Assert.Empty(v!.Items);
    }

    [Fact]
    public void Json_Object_With_OpenEnded_Date_Parses()
    {
        var v = Deserialize("""[{"ref":"h1","date":"1991-"}]""");
        Assert.NotNull(v);
        Assert.Single(v!.Items);
        Assert.Equal("h1", v.Items[0].Ref);
        Assert.Equal(7, v.Items[0].Date!.Modifier);
        Assert.Equal(1991, v.Items[0].Date!.Year);
    }

    [Fact]
    public void Shorthand_With_OpenEnded_Date_Parses()
    {
        var v = Deserialize("\"h1::1991-\"");
        Assert.NotNull(v);
        Assert.Single(v!.Items);
        Assert.Equal(7, v.Items[0].Date!.Modifier);
        Assert.Equal(1991, v.Items[0].Date!.Year);
    }
}
