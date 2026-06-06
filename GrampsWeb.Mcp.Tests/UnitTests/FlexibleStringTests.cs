using System.Text.Json;
using GrampsWeb.Mcp.Input;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

public class FlexibleStringTests
{
    private static FlexibleString? Deserialize(string json)
        => JsonSerializer.Deserialize<FlexibleString?>(json);

    [Theory]
    [InlineData("\"5\"", "5")]
    [InlineData("\"10\"", "10")]
    [InlineData("\"123\"", "123")]
    [InlineData("\"p. 5\"", "p. 5")]
    [InlineData("\"vol. 3\"", "vol. 3")]
    [InlineData("5", "5")]
    [InlineData("10", "10")]
    [InlineData("123", "123")]
    public void Deserializes_String_And_Number_As_Text(string json, string expected)
    {
        var value = Deserialize(json);

        Assert.NotNull(value);
        Assert.Equal(expected, value!.Value);
    }

    [Fact]
    public void Null_Json_Yields_Null()
    {
        Assert.Null(Deserialize("null"));
    }
}
