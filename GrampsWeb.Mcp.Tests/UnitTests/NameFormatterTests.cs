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

    [Fact]
    public void FormatName_AllSurnameTypesKeepListOrder()
    {
        var name = new GrampsName
        {
            Type = "Birth Name",
            FirstName = "Петр",
            SurnameList = new[]
            {
                new GrampsSurname
                {
                    Surname = "Соколов",
                    OriginType = "Inherited",
                    Primary = true
                },
                new GrampsSurname
                {
                    Surname = "Иванович",
                    OriginType = "Patronymic",
                    Primary = false
                }
            }
        };

        var result = GrampsValueFormatter.FormatName(name);

        Assert.Equal("Петр Соколов Иванович", result);
    }

    [Fact]
    public void FormatNameDetailed_ResolvesNumericOriginType()
    {
        var name = new GrampsName
        {
            FirstName = "Ivan",
            SurnameList = new[]
            {
                new GrampsSurname
                {
                    Surname = "Petrov",
                    OriginType = "5",
                    Primary = true
                }
            }
        };

        var result = GrampsValueFormatter.FormatNameDetailed(name, originTypeLabels: OriginTypeLabels);

        Assert.Contains("[primary, Patronymic]", result);
    }

    [Theory]
    [InlineData("0", "Custom")]
    [InlineData("2", "Inherited")]
    [InlineData("3", "Given")]
    [InlineData("4", "Taken")]
    [InlineData("5", "Patronymic")]
    [InlineData("6", "Matronymic")]
    [InlineData("7", "Feudal")]
    [InlineData("8", "Pseudonym")]
    [InlineData("9", "Patrilineal")]
    [InlineData("10", "Matrilineal")]
    [InlineData("11", "Occupation")]
    [InlineData("12", "Location")]
    public void FormatNameDetailed_ResolvesBuiltInNumericOriginTypes(string stored, string expected)
    {
        var name = new GrampsName
        {
            SurnameList =
            [
                new GrampsSurname
                {
                    Surname = "Example",
                    OriginType = stored,
                    Primary = true
                }
            ]
        };

        var result = GrampsValueFormatter.FormatNameDetailed(name, originTypeLabels: OriginTypeLabels);

        Assert.Contains($"[primary, {expected}]", result);
    }

    [Fact]
    public void FormatNameDetailed_ResolvesCustomOriginTypeFromProvidedLabels()
    {
        var name = new GrampsName
        {
            SurnameList =
            [
                new GrampsSurname
                {
                    Surname = "Example",
                    OriginType = "13",
                    Primary = true
                }
            ]
        };

        var labels = OriginTypeLabels.Concat(["Clan Name"]).ToArray();
        var result = GrampsValueFormatter.FormatNameDetailed(name, originTypeLabels: labels);

        Assert.Contains("[primary, Clan Name]", result);
    }

    [Fact]
    public void FormatNameDetailed_NumericOriginTypeWithoutLabels_KeepsRawIndex()
    {
        var name = new GrampsName
        {
            SurnameList =
            [
                new GrampsSurname
                {
                    Surname = "Example",
                    OriginType = "5",
                    Primary = true
                }
            ]
        };

        var result = GrampsValueFormatter.FormatNameDetailed(name);

        Assert.Contains("[primary, 5]", result);
    }

    [Fact]
    public void FormatName_MultipleNonPatronymicSurnameTypesKeepListOrder()
    {
        var name = new GrampsName
        {
            FirstName = "Maria",
            SurnameList =
            [
                new GrampsSurname
                {
                    Surname = "Garcia",
                    OriginType = "Patrilineal",
                    Primary = true
                },
                new GrampsSurname
                {
                    Surname = "Lopez",
                    OriginType = "Matrilineal",
                    Primary = false
                }
            ]
        };

        var result = GrampsValueFormatter.FormatName(name);

        Assert.Equal("Maria Garcia Lopez", result);
    }

    [Fact]
    public void FormatName_PreservesPrefixAndConnectorAroundSurname()
    {
        var name = new GrampsName
        {
            FirstName = "Ludwig",
            SurnameList =
            [
                new GrampsSurname
                {
                    Surname = "Beethoven",
                    Prefix = "van",
                    Connector = "und",
                    OriginType = "Inherited",
                    Primary = true
                }
            ]
        };

        var result = GrampsValueFormatter.FormatName(name);

        Assert.Equal("Ludwig van Beethoven und", result);
    }

    private static readonly string[] OriginTypeLabels =
    [
        "Custom",
        "",
        "Inherited",
        "Given",
        "Taken",
        "Patronymic",
        "Matronymic",
        "Feudal",
        "Pseudonym",
        "Patrilineal",
        "Matrilineal",
        "Occupation",
        "Location"
    ];
}
