using Xunit;
using GrampsWeb.Mcp.Formatters;
using GrampsWeb.Mcp.Models;

namespace GrampsWeb.Mcp.Tests.UnitTests;

/// <summary>
/// Unit tests for GrampsValueFormatter name formatting.
/// Tests multiple surnames with prefixes, connectors, titles, and call names.
/// </summary>
public class NameFormatterTests
{
    [Fact]
    public void FormatName_MultipleSurnames_IncludesConnectorAndPrefix()
    {
        // Arrange
        var name = new GrampsName
        {
            Type = "Birth Name",
            FirstName = "Edwin Jose",
            Call = "Jose",
            Nick = "Ed",
            FamNick = "Underhills",
            Title = "Dr.",
            Suffix = "Sr.",
            SurnameList = new[]
            {
                new GrampsSurname
                {
                    Surname = "Smith and Weston",
                    Prefix = "von der",
                    Connector = "and",
                    OriginType = "Inherited",
                    Primary = true
                },
                new GrampsSurname
                {
                    Surname = "Wilson",
                    Prefix = "",
                    Connector = "",
                    OriginType = "Patronymic",
                    Primary = false
                }
            }
        };

        // Act
        var result = GrampsValueFormatter.FormatName(name);

        // Assert
        Assert.Contains("Dr.", result);
        Assert.Contains("Edwin Jose", result);
        Assert.Contains("von der", result);
        Assert.Contains("Smith and Weston", result);
        Assert.Contains("Wilson", result);
        Assert.Contains("Sr.", result);
        Assert.Contains("Jose", result);  // Call name
        Assert.Contains("Ed", result);     // Nick name
    }

    [Fact]
    public void FormatName_WithTitle_TitleAppearsFirst()
    {
        // Arrange
        var name = new GrampsName
        {
            Type = "Birth Name",
            FirstName = "John",
            Title = "Rev.",
            SurnameList = new[]
            {
                new GrampsSurname
                {
                    Surname = "Smith",
                    Primary = true
                }
            }
        };

        // Act
        var result = GrampsValueFormatter.FormatName(name);

        // Assert
        var titlePos = result.IndexOf("Rev.");
        var johnPos = result.IndexOf("John");
        Assert.True(titlePos < johnPos, "Title should appear before first name");
    }

    [Fact]
    public void FormatName_WithCall_DisplaysInParens()
    {
        // Arrange
        var name = new GrampsName
        {
            Type = "Birth Name",
            FirstName = "Joseph",
            Call = "Joe",
            SurnameList = new[]
            {
                new GrampsSurname
                {
                    Surname = "Brown",
                    Primary = true
                }
            }
        };

        // Act
        var result = GrampsValueFormatter.FormatName(name);

        // Assert
        Assert.Contains("(Joe)", result);
    }

    [Fact]
    public void FormatName_SingleSurname_FormatsCorrectly()
    {
        // Arrange
        var name = new GrampsName
        {
            Type = "Birth Name",
            FirstName = "Mary",
            SurnameList = new[]
            {
                new GrampsSurname
                {
                    Surname = "Anderson",
                    Primary = true
                }
            }
        };

        // Act
        var result = GrampsValueFormatter.FormatName(name);

        // Assert
        Assert.Contains("Mary", result);
        Assert.Contains("Anderson", result);
    }

    [Fact]
    public void FormatName_Null_ReturnsUnknown()
    {
        // Act
        var result = GrampsValueFormatter.FormatName(null);

        // Assert
        Assert.Equal("Unknown", result);
    }

    [Fact]
    public void FormatName_WithPrefix_IncludesPrefix()
    {
        // Arrange
        var name = new GrampsName
        {
            Type = "Birth Name",
            FirstName = "Napoleon",
            SurnameList = new[]
            {
                new GrampsSurname
                {
                    Surname = "Bonaparte",
                    Prefix = "de",
                    Primary = true
                }
            }
        };

        // Act
        var result = GrampsValueFormatter.FormatName(name);

        // Assert
        Assert.Contains("de", result);
        Assert.Contains("Bonaparte", result);
    }
}
