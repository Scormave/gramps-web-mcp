using GrampsWeb.Mcp.Prompts;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

public class GrampsPromptsTests
{
    [Fact]
    public void AddPerson_KeepsPlaceWithoutDate()
    {
        var text = GrampsPrompts.AddPerson("Test Person", birthPlace: "Birth City", deathPlace: "Death City");

        Assert.Contains("Birth: place: Birth City", text);
        Assert.Contains("Death: place: Death City", text);
        Assert.Contains("one create_records call", text);
        Assert.Contains("notDuplicateOf", text);
        Assert.DoesNotContain("create_person", text);
        Assert.DoesNotContain("quick_add_person", text);
    }

    [Fact]
    public void ResearchPerson_UsesConditionalExpensiveReads()
    {
        var text = GrampsPrompts.ResearchPerson("I0001");

        Assert.Contains("get_records", text);
        Assert.Contains("only for chronological questions", text);
        Assert.Contains("only for the requested branch", text);
    }

    [Fact]
    public void ChangeLink_ResolvesExistingRecordsAndUsesIncrementalMode()
    {
        var text = GrampsPrompts.ChangeLink("person", "I0001", "eventRefs", "E0001", "remove");

        Assert.Contains("linkMode: \"add\" or \"remove\"", text);
        Assert.Contains("do not guess unknown relationship facts", text);
        Assert.Contains("Do not create a new record", text);
        Assert.Contains("section: \"link_updates\"", text);
    }

    [Fact]
    public void AddFamily_LeavesTheRelationshipToTheSources()
    {
        var text = GrampsPrompts.AddFamily("I0001", "I0002");

        Assert.DoesNotContain("Relationship:", text);
        Assert.Contains("Married when a source records the marriage", text);
        Assert.Contains("Relationship: Unmarried", GrampsPrompts.AddFamily("I0001", "I0002", "Unmarried"));
    }

    [Fact]
    public void CiteFact_ReusesSourcesAndAttachesCitation()
    {
        var text = GrampsPrompts.CiteFact("event", "E0001", "Archive", "12");

        Assert.Contains("Page/location: 12", text);
        Assert.Contains("Reuse a matching source", text);
        Assert.Contains("create_records(citations:", text);
        Assert.Contains("citationHandles: [CITATION_HANDLE], linkMode: \"add\"", text);
    }

    [Fact]
    public void AuditTree_ReportsWithoutChangingTheTree()
    {
        var text = GrampsPrompts.AuditTree();

        Assert.Contains("change nothing in the tree", text);
        Assert.Contains("leave BRANCH out", text);
        Assert.Contains("find_duplicates", text);
        Assert.Contains("\"name\": \"Disconnected\"", text);
        Assert.Contains("only after the user confirms it", text);
        Assert.DoesNotContain("own ancestor", text);
        Assert.DoesNotContain("Run only these checks", text);
    }

    [Fact]
    public void AuditTree_KeepsTheChecksInsideThePersonsBranch()
    {
        var text = GrampsPrompts.AuditTree("I0001", "dates, sources");

        Assert.Contains("Audit the branch of \"I0001\"", text);
        Assert.Contains("Run only these checks: dates, sources.", text);
        Assert.Contains("\"IsDescendantFamilyOf\", \"values\": [ID, true]", text);
        Assert.Contains("their own ancestor", text);
        Assert.DoesNotContain("IsDefaultPerson", text);
    }
}
