using System.Text.Json;
using GrampsWeb.Mcp.Input;
using GrampsWeb.Mcp.Serialization;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

public class FlexibleAddressListTests
{
    private static FlexibleAddressList? Deserialize(string json)
        => JsonSerializer.Deserialize<FlexibleAddressList?>(json);

    [Fact]
    public void Null_Yields_Null() => Assert.Null(Deserialize("null"));

    [Fact]
    public void Single_Plain_Line_Is_Street()
    {
        var v = Deserialize("\"123 Main St\"");
        Assert.NotNull(v);
        Assert.Single(v!.Items);
        Assert.Equal("123 Main St", v.Items[0].Street);
    }

    [Fact]
    public void Keyed_Block()
    {
        var json = JsonSerializer.Serialize("street: 1 Oak\nCity: Boston\nstate: MA\npostal: 02101");
        var v = Deserialize(json);
        Assert.NotNull(v);
        Assert.Single(v!.Items);
        Assert.Equal("1 Oak", v.Items[0].Street);
        Assert.Equal("Boston", v.Items[0].City);
        Assert.Equal("MA", v.Items[0].State);
        Assert.Equal("02101", v.Items[0].Postal);
    }

    [Fact]
    public void Two_Blocks_Separated_By_Blank_Line()
    {
        var s = "street: A\n\ncity: B";
        var json = JsonSerializer.Serialize(s);
        var v = Deserialize(json);
        Assert.Equal(2, v!.Items.Length);
        Assert.Equal("A", v.Items[0].Street);
        Assert.Equal("B", v.Items[1].City);
    }

    [Fact]
    public void SplitAddressBlocks_Respects_Triple_Dash_Line()
    {
        var blocks = FlexibleAddressListJsonConverter.SplitAddressBlocks("street: x\n---\ncity: y");
        Assert.Equal(2, blocks.Count);
    }

    [Fact]
    public void Unknown_Key_Throws()
    {
        var json = JsonSerializer.Serialize("foo: bar");
        Assert.Throws<JsonException>(() => Deserialize(json));
    }
}
