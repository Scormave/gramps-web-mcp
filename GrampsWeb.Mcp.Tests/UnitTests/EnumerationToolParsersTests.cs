using GrampsWeb.Mcp.Tools.Parsing;
using ModelContextProtocol;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

public class EnumerationToolParsersTests
{
    [Theory]
    [InlineData("Female", 0)]
    [InlineData("female", 0)]
    [InlineData("Male", 1)]
    [InlineData("UNKNOWN", 2)]
    public void GrampsGenderParser_ParseRequired(string input, int expected)
        => Assert.Equal(expected, GrampsGenderParser.ParseRequired(input));

    [Fact]
    public void GrampsGenderParser_ParseRequired_Throws_OnInvalid()
    {
        var ex = Assert.Throws<McpException>(() => GrampsGenderParser.ParseRequired("Other"));
        Assert.Contains("Female", ex.Message);
    }

    [Fact]
    public void GrampsGenderParser_ParseOptional_NullOrWhitespace_ReturnsNull()
    {
        Assert.Null(GrampsGenderParser.ParseOptional(null));
        Assert.Null(GrampsGenderParser.ParseOptional("  "));
    }

    [Theory]
    [InlineData("Plain", 0)]
    [InlineData("html", 1)]
    [InlineData("Formatted", 1)]
    public void NoteTextFormatParser_ParseRequired(string input, int expected)
        => Assert.Equal(expected, NoteTextFormatParser.ParseRequired(input));

    [Theory]
    [InlineData("Very Low", 0)]
    [InlineData("low", 1)]
    [InlineData("Normal", 2)]
    [InlineData("HIGH", 3)]
    [InlineData("veryhigh", 4)]
    public void CitationConfidenceParser_ParseRequired(string input, int expected)
        => Assert.Equal(expected, CitationConfidenceParser.ParseRequired(input));
}
