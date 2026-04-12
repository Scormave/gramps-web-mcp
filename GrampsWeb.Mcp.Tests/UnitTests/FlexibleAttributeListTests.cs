using System.Text.Json;
using GrampsWeb.Mcp.Input;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

public class FlexibleAttributeListTests
{
    private static FlexibleAttributeList? Deserialize(string json)
        => JsonSerializer.Deserialize<FlexibleAttributeList?>(json);

    [Fact]
    public void Null_Yields_Null()
    {
        Assert.Null(Deserialize("null"));
    }

    [Fact]
    public void Empty_Array_Yields_Empty_Items()
    {
        var v = Deserialize("[]");
        Assert.NotNull(v);
        Assert.Empty(v!.Items);
    }

    [Fact]
    public void Json_Objects_Array()
    {
        var v = Deserialize("""[{"type":"Nick","value":"Joe"},{"type":"Occupation","value":"Smith"}]""");
        Assert.NotNull(v);
        Assert.Equal(2, v!.Items.Length);
        Assert.Equal("Nick", v.Items[0].Type);
        Assert.Equal("Joe", v.Items[0].Value);
    }

    [Fact]
    public void Json_Strings_Type_Value()
    {
        var v = Deserialize("""["Nickname: Test Nickname","Role: Witness"]""");
        Assert.NotNull(v);
        Assert.Equal("Nickname", v.Items[0].Type);
        Assert.Equal("Test Nickname", v.Items[0].Value);
        Assert.Equal("Witness", v.Items[1].Value);
    }

    [Fact]
    public void Single_String_Multiline()
    {
        var v = Deserialize("\"Occupation: Farmer\\nNote: extra: colons\"");
        Assert.NotNull(v);
        Assert.Equal(2, v.Items.Length);
        Assert.Equal("extra: colons", v.Items[1].Value);
    }

    [Fact]
    public void Single_String_Pipe_Separated()
    {
        var v = Deserialize("\"A: 1|B: 2\"");
        Assert.NotNull(v);
        Assert.Equal("1", v.Items[0].Value);
        Assert.Equal("B", v.Items[1].Type);
    }

    [Fact]
    public void Embedded_Json_Array_String()
    {
        var v = Deserialize("\"[{\\\"type\\\":\\\"T\\\",\\\"value\\\":\\\"V\\\"}]\"");
        Assert.NotNull(v);
        Assert.Single(v!.Items);
        Assert.Equal("T", v.Items[0].Type);
    }

    [Fact]
    public void Whitespace_String_Yields_Null()
    {
        Assert.Null(Deserialize("\"   \""));
    }

    [Fact]
    public void Missing_Colon_Throws()
    {
        Assert.Throws<JsonException>(() => Deserialize("""["no colon here"]"""));
    }
}
