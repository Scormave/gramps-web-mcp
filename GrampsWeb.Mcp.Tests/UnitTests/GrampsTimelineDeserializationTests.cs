using System.Text.Json;
using GrampsWeb.Mcp.Models;
using GrampsWeb.Mcp.Serialization;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

public class GrampsTimelineDeserializationTests
{
    [Fact]
    public void Deserialize_TimelineEventProfile_StringDateAndPlaceObject()
    {
        const string json = """
            [{
              "handle": "evt1",
              "gramps_id": "E1",
              "date": "1857-05-30",
              "label": "Birth",
              "type": "Birth",
              "place": { "display_name": "Somewhere", "name": "Somewhere", "handle": "pl1" },
              "description": "",
              "role": "Primary",
              "age": "0 years"
            }]
            """;

        var entries = JsonSerializer.Deserialize<GrampsTimelineEntry[]>(json, GrampsJson.Options);
        Assert.NotNull(entries);
        Assert.Single(entries);
        var e = entries![0];
        Assert.Equal("1857-05-30", e.Date);
        Assert.Equal("Birth", e.Label);
        Assert.Equal("Birth", e.Type);
        Assert.NotNull(e.Place);
        Assert.Equal("Somewhere", e.Place!.DisplayName);
    }
}
