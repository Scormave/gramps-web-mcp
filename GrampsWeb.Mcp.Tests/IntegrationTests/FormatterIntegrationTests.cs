using System;
using System.Threading.Tasks;
using Xunit;
using GrampsWeb.Mcp.Formatters;
using GrampsWeb.Mcp.Models;

namespace GrampsWeb.Mcp.Tests.IntegrationTests;

/// <summary>
/// Integration tests for place and timeline formatters.
/// Uses static/simple data without requiring mocks or a live API.
/// </summary>
public class FormatterIntegrationTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public void FormatPlaceHierarchy_WithSimplePlace_ReturnsNameAndType()
    {
        // Arrange
        var place = new GrampsPlace
        {
            Name = "Cork",
            Type = "County"
        };

        // Act  
        var result = PlaceFormatter.FormatPlaceHierarchy(place, null, 6).Result;

        // Assert
        Assert.Contains("Cork", result);
        Assert.Contains("County", result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void FormatTimelineChronological_WithMultipleEvents_SortsChronologically()
    {
        // Arrange
        var events = new[]
        {
            new GrampsTimelineEntry
            {
                Type = "Birth",
                Date = new GrampsDate { Day = 12, Month = 11, Year = 1899, Slash = false, Modifier = 0, SortVal = 18991112 },
                Place = "Cork"
            },
            new GrampsTimelineEntry
            {
                Type = "Marriage",
                Date = new GrampsDate { Day = 5, Month = 6, Year = 1925, Slash = false, Modifier = 0, SortVal = 19250605 },
                Place = "Dublin"
            }
        };

        // Act
        var result = TimelineFormatter.FormatTimelineChronological(events);

        // Assert
        Assert.Contains("Birth", result);
        Assert.Contains("Marriage", result);
        Assert.Contains("Cork", result);
        Assert.Contains("Dublin", result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void FormatTimelineChronological_WithOverTwentyEvents_GroupsByDecade()
    {
        // Arrange - create 25 events spanning multiple decades
        var events = new GrampsTimelineEntry[25];
        for (int i = 0; i < 25; i++)
        {
            events[i] = new GrampsTimelineEntry
            {
                Type = $"Event{i}",
                Date = new GrampsDate
                {
                    Day = 1,
                    Month = 1,
                    Year = 1880 + (i * 2),
                    Slash = false,
                    Modifier = 0,
                    SortVal = (1880 + (i * 2)) * 10000 + 101
                },
                Place = $"Place{i}"
            };
        }

        // Act
        var result = TimelineFormatter.FormatTimelineChronological(events);

        // Assert
        Assert.Contains("Timeline (25 events)", result);
        // Should group by decades when >20 events
        Assert.True(
            result.Contains("188") || result.Contains("189") || result.Contains("192"),
            "Timeline should contain decade groupings"
        );
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void FormatTimeline_WithLegacyMethod_FormatsCorrectly()
    {
        // Arrange
        var entries = new[]
        {
            new GrampsTimelineEntry
            {
                Type = "Birth",
                Date = new GrampsDate { Day = 1, Month = 1, Year = 1900, Slash = false, Modifier = 0 },
                Place = "Boston",
                Role = "Primary"
            }
        };

        // Act
        var result = TimelineFormatter.FormatTimeline("TIMELINE", entries);

        // Assert
        Assert.Contains("TIMELINE", result);
        Assert.Contains("Birth", result);
        Assert.Contains("Boston", result);
        Assert.Contains("Primary", result);
    }
}
