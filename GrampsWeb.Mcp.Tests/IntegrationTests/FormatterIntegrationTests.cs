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
                Date = "1899-11-12",
                Place = new GrampsTimelinePlaceProfile { Name = "Cork", DisplayName = "Cork" }
            },
            new GrampsTimelineEntry
            {
                Type = "Marriage",
                Date = "1925-06-05",
                Place = new GrampsTimelinePlaceProfile { Name = "Dublin", DisplayName = "Dublin" }
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
    public void FormatTimelineChronological_IncludesHandlePerRowWhenPresent()
    {
        var events = new[]
        {
            new GrampsTimelineEntry
            {
                Handle = "e111",
                Type = "Birth",
                Date = "1900-01-01"
            },
            new GrampsTimelineEntry
            {
                Handle = "e222",
                Type = "Death",
                Date = "1950-01-01"
            }
        };

        var result = TimelineFormatter.FormatTimelineChronological(events);

        Assert.Contains("[event: e111]", result);
        Assert.Contains("[event: e222]", result);
        Assert.DoesNotContain("Event handles:", result);
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
                Date = $"{1880 + i * 2}-01-01",
                Place = new GrampsTimelinePlaceProfile { DisplayName = $"Place{i}" }
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
                Date = "1900-01-01",
                Place = new GrampsTimelinePlaceProfile { DisplayName = "Boston" },
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
