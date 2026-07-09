using System.Text.Json;
using GrampsWeb.Mcp.Input;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

public class FlexiblePlaceNameListTests
{
    private static FlexiblePlaceNameList? Deserialize(string json)
        => JsonSerializer.Deserialize<FlexiblePlaceNameList?>(json);

    [Fact]
    public void Null_Yields_Null() => Assert.Null(Deserialize("null"));

    [Fact]
    public void Single_String_Parses()
    {
        var v = Deserialize("\"Old Name\"");
        Assert.NotNull(v);
        Assert.Single(v!.Items);
        Assert.Equal("Old Name", v.Items[0].Value);
    }

    [Fact]
    public void String_With_Lang_Parses()
    {
        var v = Deserialize("\"Old Name::de\"");
        Assert.NotNull(v);
        Assert.Single(v!.Items);
        Assert.Equal("Old Name", v.Items[0].Value);
        Assert.Equal("de", v.Items[0].Lang);
    }

    [Fact]
    public void Json_Object_With_Date_Parses()
    {
        var v = Deserialize("""[{"value":"St. Petersburg","lang":"ru","date":"1914-1924"}]""");
        Assert.NotNull(v);
        Assert.Single(v!.Items);
        Assert.Equal("St. Petersburg", v.Items[0].Value);
        Assert.Equal("ru", v.Items[0].Lang);
        Assert.NotNull(v.Items[0].Date);
        Assert.Equal(1914, v.Items[0].Date!.Year);
        Assert.Equal(1924, v.Items[0].Date!.EndYear);
    }

    [Fact]
    public void Multiline_String_Parses_Multiple()
    {
        var v = Deserialize("\"Name One\\nName Two::de\"");
        Assert.NotNull(v);
        Assert.Equal(2, v!.Items.Length);
        Assert.Equal("Name One", v.Items[0].Value);
        Assert.Equal("Name Two", v.Items[1].Value);
        Assert.Equal("de", v.Items[1].Lang);
    }
}
