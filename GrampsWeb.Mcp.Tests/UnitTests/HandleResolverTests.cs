using GrampsWeb.Mcp.Client;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

public class HandleResolverTests
{
    [Theory]
    [InlineData("I0001", true)]
    [InlineData("F23", true)]
    [InlineData("P0005", true)]
    [InlineData("E1", true)]
    [InlineData("S999", true)]
    [InlineData("C0001", true)]
    [InlineData("R0001", true)]
    [InlineData("N0001", true)]
    [InlineData("M0001", true)]
    [InlineData("T0001", true)]
    [InlineData("abc123def456789012345678", false)]
    [InlineData("", false)]
    [InlineData("0001", false)]
    [InlineData("II001", false)]
    [InlineData("i0001", false)]
    [InlineData("A", false)]
    [InlineData("I", false)]
    [InlineData("I000000001", false)] // 10 chars, exceeds typical Gramps ID length
    public void LooksLikeGrampsId_DetectsCorrectly(string value, bool expected)
    {
        Assert.Equal(expected, HandleResolver.LooksLikeGrampsId(value));
    }

    [Theory]
    [InlineData('I', "people")]
    [InlineData('F', "families")]
    [InlineData('E', "events")]
    [InlineData('P', "places")]
    [InlineData('S', "sources")]
    [InlineData('C', "citations")]
    [InlineData('R', "repositories")]
    [InlineData('N', "notes")]
    [InlineData('M', "media")]
    [InlineData('T', "tags")]
    [InlineData('Z', null)]
    [InlineData('X', null)]
    [InlineData('i', null)]
    public void PrefixToObjectType_MapsCorrectly(char prefix, string? expected)
    {
        Assert.Equal(expected, HandleResolver.PrefixToObjectType(prefix));
    }
}
