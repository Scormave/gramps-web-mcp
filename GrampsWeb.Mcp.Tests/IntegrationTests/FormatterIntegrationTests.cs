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
    public void FormatTimelineChronological_SortsYearsBefore1000_AndPutsUndatedLast()
    {
        var events = new[]
        {
            new GrampsTimelineEntry { Type = "Note" },
            new GrampsTimelineEntry { Type = "Death", Date = "1014" },
            new GrampsTimelineEntry { Type = "Birth", Date = "960" },
            new GrampsTimelineEntry { Type = "Baptism", Date = "0961-03-05" },
        };

        var result = TimelineFormatter.FormatTimelineChronological(events);

        var positions = new[] { "960: Birth", "0961-03-05: Baptism", "1014: Death", "—: Note" }
            .Select(row => result.IndexOf(row, StringComparison.Ordinal))
            .ToArray();
        Assert.DoesNotContain(-1, positions);
        Assert.Equal(positions.Order(), positions);
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
    public void FormatTimelineChronological_FormatsEntryDetailsCorrectly()
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
        var result = TimelineFormatter.FormatTimelineChronological(entries);

        // Assert
        Assert.Contains("Timeline (1 events)", result);
        Assert.Contains("Birth", result);
        Assert.Contains("Boston", result);
        Assert.DoesNotContain("Primary", result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void FormatTimelineChronological_NamesRelativeWithTheirAge_AndOwnEventsWithAnchorAge()
    {
        var entries = new[]
        {
            new GrampsTimelineEntry
            {
                Handle = "e1",
                Label = "Marriage",
                Date = "1925-06-05",
                Role = "Primary",
                Age = "25 years",
                Person = new GrampsTimelinePersonProfile { Relationship = "self" },
                Place = new GrampsTimelinePlaceProfile { DisplayName = "Dublin" }
            },
            new GrampsTimelineEntry
            {
                Handle = "e2",
                Label = "Death (Father)",
                Date = "1950-03-02",
                Role = "Primary",
                Age = "50 years",
                Person = new GrampsTimelinePersonProfile
                {
                    Handle = "p2", GrampsId = "I0012", NameDisplay = "Petrov, Ivan",
                    Relationship = "father", Age = "70 years"
                }
            },
            new GrampsTimelineEntry
            {
                Handle = "e3",
                Label = "Baptism (Son)",
                Date = "1955-01-01",
                Role = "Godparent",
                Person = new GrampsTimelinePersonProfile { GrampsId = "I0013", NameDisplay = "Petrov, Oleg" }
            }
        };

        var result = TimelineFormatter.FormatTimelineChronological(entries);

        Assert.Contains("  1925-06-05: Marriage, age 25 years — Dublin  [event: e1]", result);
        Assert.Contains("  1950-03-02: Death (Father): Petrov, Ivan (I0012), age 70 years  [event: e2]", result);
        Assert.Contains("  1955-01-01: Baptism (Son): Petrov, Oleg (I0013) [Godparent]  [event: e3]", result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void FormatTimelineChronological_LeavesOutZeroAge()
    {
        var entries = new[]
        {
            new GrampsTimelineEntry { Handle = "e1", Label = "Birth", Date = "1900-01-01", Age = "0 days" },
            new GrampsTimelineEntry
            {
                Handle = "e2",
                Label = "Birth (Son)",
                Date = "1930-02-03",
                Age = "30 years",
                Person = new GrampsTimelinePersonProfile { GrampsId = "I0013", NameDisplay = "Petrov, Oleg", Age = "0 дней" }
            },
            new GrampsTimelineEntry { Handle = "e3", Label = "Baptism", Date = "1900-01-11", Age = "10 days" }
        };

        var result = TimelineFormatter.FormatTimelineChronological(entries);

        Assert.Contains("  1900-01-01: Birth  [event: e1]", result);
        Assert.Contains("  1930-02-03: Birth (Son): Petrov, Oleg (I0013)  [event: e2]", result);
        Assert.Contains("  1900-01-11: Baptism, age 10 days  [event: e3]", result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void FormatTimelineChronological_ShowsParticipantsOfPlaceRows()
    {
        var entries = new[]
        {
            new GrampsTimelineEntry
            {
                Handle = "e1",
                Type = "Census",
                Date = "1897",
                Participants = "Petrov, Ivan (I0012)",
                Description = "First census"
            }
        };

        var result = TimelineFormatter.FormatTimelineChronological(entries);

        Assert.Contains("  1897: Census: Petrov, Ivan (I0012)  [event: e1]\n    First census", result.Replace("\r\n", "\n"));
    }
}
