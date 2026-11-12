using System.Text.Json;
using GrampsWeb.Mcp.Formatters;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

public class GrampsDefaultTypeLabelsTests
{
    // Part of /api/types/default/event_types/map: the list route orders the names differently.
    private const string EventTypeMap =
        """{"-1":"Unknown","0":"Custom","1":"Marriage","11":"Adopted","12":"Birth","13":"Death","22":"Christening"}""";

    [Fact]
    public void LabelsByValue_PutsEachNameAtItsValue()
    {
        var labels = GrampsDefaultTypeLabels.LabelsByValue(JsonDocument.Parse(EventTypeMap).RootElement);

        Assert.NotNull(labels);
        Assert.Equal(23, labels!.Count);
        Assert.Equal("Custom", labels[0]);
        Assert.Equal("Marriage", labels[1]);
        Assert.Equal("", labels[2]);
        Assert.Equal("Birth", labels[12]);
        Assert.Equal("Christening", labels[22]);
    }

    [Theory]
    [InlineData("[\"Unknown\",\"Birth\"]")]
    [InlineData("{}")]
    [InlineData("{\"-1\":\"Unknown\",\"x\":\"Birth\",\"5000\":\"Death\"}")]
    public void LabelsByValue_WithoutValues_IsNull(string json)
    {
        Assert.Null(GrampsDefaultTypeLabels.LabelsByValue(JsonDocument.Parse(json).RootElement));
    }

    [Theory]
    [InlineData("12", "Birth")]
    [InlineData("22", "Christening")]
    [InlineData("13", "Death")]
    [InlineData("-1", "Unknown")]
    [InlineData("2", "2")]
    [InlineData("99", "99")]
    [InlineData("Feast", "Feast")]
    [InlineData("", "—")]
    public void ResolveStored_NamesAStoredValue(string stored, string expected)
    {
        var labels = GrampsDefaultTypeLabels.LabelsByValue(JsonDocument.Parse(EventTypeMap).RootElement);

        Assert.Equal(expected, GrampsDefaultTypeLabels.ResolveStored(stored, labels));
    }

    [Fact]
    public void ResolveStored_WithoutLabels_KeepsTheNumber()
    {
        Assert.Equal("12", GrampsDefaultTypeLabels.ResolveStored("12", null));
        Assert.Equal("Unknown", GrampsDefaultTypeLabels.ResolveStored("-1", null));
    }

    [Fact]
    public void ResolveStored_NameOriginType_CountsFromCustom()
    {
        var labels = GrampsDefaultTypeLabels.LabelsByValue(JsonDocument.Parse(
            """{"-1":"Unknown","0":"Custom","1":"","2":"Inherited","3":"Given","4":"Taken","5":"Patronymic"}""").RootElement);

        Assert.Equal("Patronymic", GrampsDefaultTypeLabels.ResolveStored("5", labels));
        Assert.Equal("Inherited", GrampsDefaultTypeLabels.ResolveStored("2", labels));
    }
}
