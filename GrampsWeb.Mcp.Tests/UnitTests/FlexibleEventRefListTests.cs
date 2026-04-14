using System.Text.Json;
using GrampsWeb.Mcp.Input;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

public class FlexibleEventRefListTests
{
    private static FlexibleEventRefList? Deserialize(string json)
        => JsonSerializer.Deserialize<FlexibleEventRefList?>(json);

    [Fact]
    public void Null_Yields_Null() => Assert.Null(Deserialize("null"));

    [Fact]
    public void Single_Handle_String_Defaults_Primary()
    {
        var v = Deserialize("\"h1\"");
        Assert.NotNull(v);
        Assert.Single(v!.Items);
        Assert.Equal("h1", v.Items[0].Ref);
        Assert.Equal("Primary", v.Items[0].Role);
    }

    [Fact]
    public void Handle_With_Role_Parses()
    {
        var v = Deserialize("\"h1::Witness\"");
        Assert.NotNull(v);
        Assert.Single(v!.Items);
        Assert.Equal("h1", v.Items[0].Ref);
        Assert.Equal("Witness", v.Items[0].Role);
    }

    [Fact]
    public void Delimited_String_Parses_Multiple()
    {
        var v = Deserialize("\"h1::Primary, h2::Witness\"");
        Assert.NotNull(v);
        Assert.Equal(2, v!.Items.Length);
        Assert.Equal("h1", v.Items[0].Ref);
        Assert.Equal("Primary", v.Items[0].Role);
        Assert.Equal("h2", v.Items[1].Ref);
        Assert.Equal("Witness", v.Items[1].Role);
    }

    [Fact]
    public void Json_String_Array_Parses()
    {
        var v = Deserialize("""["h1","h2::Witness"]""");
        Assert.NotNull(v);
        Assert.Equal(2, v!.Items.Length);
        Assert.Equal("h1", v.Items[0].Ref);
        Assert.Equal("Primary", v.Items[0].Role);
        Assert.Equal("h2", v.Items[1].Ref);
        Assert.Equal("Witness", v.Items[1].Role);
    }

    [Fact]
    public void Json_Object_Array_Parses()
    {
        var v = Deserialize("""[{"ref":"h1","role":"Primary"},{"ref":"h2","role":"Witness"}]""");
        Assert.NotNull(v);
        Assert.Equal(2, v!.Items.Length);
        Assert.Equal("h1", v.Items[0].Ref);
        Assert.Equal("Primary", v.Items[0].Role);
        Assert.Equal("h2", v.Items[1].Ref);
        Assert.Equal("Witness", v.Items[1].Role);
    }

    [Fact]
    public void Empty_String_Yields_Null()
    {
        Assert.Null(Deserialize("\"   \""));
    }
}
