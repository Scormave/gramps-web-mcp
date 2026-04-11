using GrampsWeb.Mcp.Formatters;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

public class PlaceTypeDisplayFormatterTests
{
    private static readonly string[] DefaultLikeLabels = ["Custom", "Country", "State", "County", "City"];

    [Theory]
    [InlineData(null, "—")]
    [InlineData("", "—")]
    [InlineData("   ", "—")]
    [InlineData("City", "City")]
    [InlineData("Country", "Country")]
    public void ResolveStoredPlaceType_NonNumeric_ReturnsAsIsOrDash(string? stored, string expected)
    {
        Assert.Equal(expected, PlaceTypeDisplayFormatter.ResolveStoredPlaceType(stored, DefaultLikeLabels));
    }

    [Theory]
    [InlineData("0", "Custom")]
    [InlineData("1", "Country")]
    [InlineData("4", "City")]
    public void ResolveStoredPlaceType_Numeric_UsesLabelList(string stored, string expected)
    {
        Assert.Equal(expected, PlaceTypeDisplayFormatter.ResolveStoredPlaceType(stored, DefaultLikeLabels));
    }

    [Fact]
    public void ResolveStoredPlaceType_IndexOutOfRange_KeepsDigits()
    {
        Assert.Equal("99", PlaceTypeDisplayFormatter.ResolveStoredPlaceType("99", DefaultLikeLabels));
    }

    [Fact]
    public void ResolveStoredPlaceType_NullLabels_KeepsDigits()
    {
        Assert.Equal("1", PlaceTypeDisplayFormatter.ResolveStoredPlaceType("1", null));
    }

    [Theory]
    [InlineData("1", true)]
    [InlineData("01", true)]
    [InlineData("Country", false)]
    [InlineData("1a", false)]
    public void IsNumericIndex(string s, bool expected)
    {
        Assert.Equal(expected, PlaceTypeDisplayFormatter.IsNumericIndex(s));
    }
}
