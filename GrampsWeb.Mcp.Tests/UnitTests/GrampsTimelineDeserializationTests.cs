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
        Assert.Equal("0 years", e.Age);
    }

    [Fact]
    public void Deserialize_TimelineEventProfile_PersonOfRelativeEvent()
    {
        const string json = """
            [{
              "handle": "evt2",
              "label": "Death (Father)",
              "type": "Death",
              "date": "1950-03-02",
              "role": "Primary",
              "age": "50 years",
              "person": {
                "handle": "p2",
                "gramps_id": "I0012",
                "name_display": "Petrov, Ivan",
                "name_given": "Ivan",
                "sex": "M",
                "birth": {},
                "death": { "type": "Death", "date": "1950-03-02" },
                "age": "70 years",
                "relationship": "father"
              },
              "place": {}
            }]
            """;

        var e = Assert.Single(JsonSerializer.Deserialize<GrampsTimelineEntry[]>(json, GrampsJson.Options)!);
        Assert.NotNull(e.Person);
        Assert.Equal("I0012", e.Person!.GrampsId);
        Assert.Equal("Petrov, Ivan", e.Person.NameDisplay);
        Assert.Equal("father", e.Person.Relationship);
        Assert.Equal("70 years", e.Person.Age);
    }

    [Theory]
    [InlineData(", Ivan", "Ivan")]
    [InlineData("Petrov, ", "Petrov")]
    [InlineData(" , ", "")]
    public void Deserialize_TimelinePerson_TrimsSeparatorsOfEmptyNameParts(string nameDisplay, string expected)
    {
        var json = $$"""[{ "handle": "evt3", "person": { "name_display": "{{nameDisplay}}" } }]""";

        var e = Assert.Single(JsonSerializer.Deserialize<GrampsTimelineEntry[]>(json, GrampsJson.Options)!);
        Assert.Equal(expected, e.Person!.NameDisplay);
    }
}
