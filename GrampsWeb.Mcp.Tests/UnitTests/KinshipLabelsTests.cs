using GrampsWeb.Mcp.Formatters;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

public class KinshipLabelsTests
{
    [Fact]
    public void AncestorChainLabel_Father_Mother_Chain()
    {
        Assert.Equal("Father", KinshipLabels.AncestorChainLabel(new[] { true }));
        Assert.Equal("Mother", KinshipLabels.AncestorChainLabel(new[] { false }));
        Assert.Equal("Father's father", KinshipLabels.AncestorChainLabel(new[] { true, true }));
        Assert.Equal("Father's mother", KinshipLabels.AncestorChainLabel(new[] { true, false }));
        Assert.Equal("Mother's father", KinshipLabels.AncestorChainLabel(new[] { false, true }));
    }

    [Theory]
    [InlineData(1, 1, "Son")]
    [InlineData(1, 0, "Daughter")]
    [InlineData(1, 2, "Child")]
    [InlineData(2, 1, "Grandson")]
    [InlineData(2, 0, "Granddaughter")]
    [InlineData(3, 1, "Great-grandson")]
    [InlineData(4, 0, "Great-great-granddaughter")]
    public void DescendantKinshipLabel_Generations(int gen, int gender, string expected)
    {
        Assert.Equal(expected, KinshipLabels.DescendantKinshipLabel(gen, gender));
    }
}
