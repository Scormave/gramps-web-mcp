using System.Text.Json;
using GrampsWeb.Mcp.Input;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

public class FlexibleChildRefListTests
{
    private static FlexibleChildRefList? Deserialize(string json)
        => JsonSerializer.Deserialize<FlexibleChildRefList?>(json);

    [Fact]
    public void Null_Yields_Null() => Assert.Null(Deserialize("null"));

    [Fact]
    public void Single_Handle_Defaults_Birth()
    {
        var v = Deserialize("\"h1\"");
        Assert.NotNull(v);
        Assert.Single(v!.Items);
        Assert.Equal("h1", v.Items[0].Ref);
        Assert.Equal("Birth", v.Items[0].FatherRelType);
        Assert.Equal("Birth", v.Items[0].MotherRelType);
    }

    [Fact]
    public void Handle_With_Relation_Parses()
    {
        var v = Deserialize("\"h1::Adopted\"");
        Assert.NotNull(v);
        Assert.Single(v!.Items);
        Assert.Equal("h1", v.Items[0].Ref);
        Assert.Equal("Adopted", v.Items[0].FatherRelType);
        Assert.Equal("Adopted", v.Items[0].MotherRelType);
    }

    [Fact]
    public void Delimited_String_Parses_Multiple()
    {
        var v = Deserialize("\"h1::Birth, h2::Stepchild\"");
        Assert.NotNull(v);
        Assert.Equal(2, v!.Items.Length);
        Assert.Equal("h1", v.Items[0].Ref);
        Assert.Equal("Birth", v.Items[0].FatherRelType);
        Assert.Equal("h2", v.Items[1].Ref);
        Assert.Equal("Stepchild", v.Items[1].FatherRelType);
        Assert.Equal("Stepchild", v.Items[1].MotherRelType);
    }

    [Fact]
    public void Json_String_Array_Parses()
    {
        var v = Deserialize("""["h1","h2::Adopted"]""");
        Assert.NotNull(v);
        Assert.Equal(2, v!.Items.Length);
        Assert.Equal("h1", v.Items[0].Ref);
        Assert.Equal("Birth", v.Items[0].FatherRelType);
        Assert.Equal("h2", v.Items[1].Ref);
        Assert.Equal("Adopted", v.Items[1].FatherRelType);
        Assert.Equal("Adopted", v.Items[1].MotherRelType);
    }

    [Fact]
    public void Json_Object_Array_Parses()
    {
        var v = Deserialize("""[{"ref":"h1","frel":"Birth","mrel":"Birth"}]""");
        Assert.NotNull(v);
        Assert.Single(v!.Items);
        Assert.Equal("h1", v.Items[0].Ref);
        Assert.Equal("Birth", v.Items[0].FatherRelType);
        Assert.Equal("Birth", v.Items[0].MotherRelType);
    }
}
