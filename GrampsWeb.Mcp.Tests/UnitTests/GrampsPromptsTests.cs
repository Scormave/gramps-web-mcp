using GrampsWeb.Mcp.Prompts;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

public class GrampsPromptsTests
{
    [Fact]
    public void AddPerson_KeepsPlaceWithoutDate()
    {
        var text = GrampsPrompts.AddPerson("Test Person", birthPlace: "Birth City", deathPlace: "Death City").Text;

        Assert.Contains("Birth: place: Birth City", text);
        Assert.Contains("Death: place: Death City", text);
        Assert.Contains("create_event", text);
        Assert.Contains("create_person", text);
        Assert.DoesNotContain("quick_add_person", text);
    }

    [Fact]
    public void ResearchPerson_UsesConditionalExpensiveReads()
    {
        var text = GrampsPrompts.ResearchPerson("I0001").Text;

        Assert.Contains("get_object", text);
        Assert.Contains("only for chronological questions", text);
        Assert.Contains("only for the requested branch", text);
    }

    [Fact]
    public void ChangeLink_ResolvesExistingRecordsAndUsesIncrementalMode()
    {
        var text = GrampsPrompts.ChangeLink("person", "I0001", "eventRefs", "E0001", "remove").Text;

        Assert.Contains("linkMode: \"add\" or \"remove\"", text);
        Assert.Contains("do not guess unknown relationship facts", text);
        Assert.Contains("Do not create a new record", text);
        Assert.Contains("section: \"link_updates\"", text);
    }

    [Fact]
    public void CiteFact_ReusesSourcesAndAttachesCitation()
    {
        var text = GrampsPrompts.CiteFact("event", "E0001", "Archive", "12").Text;

        Assert.Contains("Page/location: 12", text);
        Assert.Contains("Reuse a matching source", text);
        Assert.Contains("create_citation", text);
        Assert.Contains("citationHandles: [CITATION_HANDLE], linkMode: \"add\"", text);
    }
}
