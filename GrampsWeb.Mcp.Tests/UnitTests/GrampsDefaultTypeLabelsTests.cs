using GrampsWeb.Mcp.Formatters;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

public class GrampsDefaultTypeLabelsTests
{
    [Fact]
    public void MergeDefaultAndCustomLabels_ConcatsDefaultThenCustom()
    {
        var def = new[] { "General", "Transcript", "Research" };
        var custom = new[] { "", "Person Note", "" };
        var merged = GrampsDefaultTypeLabels.MergeDefaultAndCustomLabels(def, custom);
        Assert.NotNull(merged);
        Assert.Equal(6, merged!.Count);
        Assert.Equal("General", merged[0]);
        Assert.Equal("Transcript", merged[1]);
        Assert.Equal("Research", merged[2]);
        Assert.Equal("", merged[3]);
        Assert.Equal("Person Note", merged[4]);
        Assert.Equal("", merged[5]);
    }

    [Fact]
    public void MergeDefaultAndCustomLabels_AppendsAllCustomEntries()
    {
        var def = new[] { "A", "B" };
        var custom = new[] { "", "", "Extra" };
        var merged = GrampsDefaultTypeLabels.MergeDefaultAndCustomLabels(def, custom);
        Assert.NotNull(merged);
        Assert.Equal(5, merged!.Count);
        Assert.Equal("A", merged[0]);
        Assert.Equal("B", merged[1]);
        Assert.Equal("", merged[2]);
        Assert.Equal("", merged[3]);
        Assert.Equal("Extra", merged[4]);
    }

    [Fact]
    public void ResolveStored_NumericIndexUnknown_ReturnsRawIndex()
    {
        var labels = new[] { "General", "Transcript" };
        Assert.Equal("99", GrampsDefaultTypeLabels.ResolveStored("99", labels));
    }

    [Fact]
    public void ResolveStored_NonNumeric_ReturnsAsIs()
    {
        var labels = new[] { "General" };
        Assert.Equal("Person Note", GrampsDefaultTypeLabels.ResolveStored("Person Note", labels));
    }

    [Fact]
    public void ResolveStoredWithDefaultAndCustomLists_NumericIndexIntoConcatenatedList()
    {
        var def = new[] { "A", "B" };
        var custom = new[] { "X" };
        var r = GrampsDefaultTypeLabels.ResolveStoredWithDefaultAndCustomLists("2", def, custom);
        Assert.Equal("X", r);
    }

    [Fact]
    public void ResolveStoredWithDefaultAndCustomLists_CustomNameFromWireString_Unchanged()
    {
        var def = new[] { "General" };
        var custom = new[] { "Person Note" };
        Assert.Equal(
            "Test",
            GrampsDefaultTypeLabels.ResolveStoredWithDefaultAndCustomLists("Test", def, custom));
    }
}
