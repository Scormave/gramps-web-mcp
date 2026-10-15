using Xunit;

namespace GrampsWeb.Mcp.IntegrationTests;

/// <summary>search, list_objects, get_object, get_recent_changes, get_bookmarks and get_reference.</summary>
[Collection(GrampsWebCollection.Name)]
public sealed class ReadToolTests(GrampsWebFixture gramps)
{
    private McpSession Session => gramps.Session;

    [IntegrationFact]
    public async Task Search_FindsPersonBySurname()
    {
        var surname = TestNames.Surname();
        var person = await Session.CreateAsync("create_person", new { primaryName = new { first = "Ivan", surname } });

        var found = await Session.CallAsync("search", new { query = surname });

        Assert.Contains("Total: 1", found);
        Assert.Contains($"Person: {surname}, Ivan — handle: {person.Handle} | gramps_id: {person.GrampsId}", found);
    }

    [IntegrationFact]
    public async Task ListObjects_FiltersWithGql()
    {
        var first = TestNames.Surname();
        var match = await Session.CreateAsync("create_person", new { primaryName = new { first, surname = TestNames.Surname() } });
        await Session.CreateAsync("create_person", new { primaryName = new { first = TestNames.Surname(), surname = TestNames.Surname() } });

        var listed = await Session.CallAsync("list_objects", new { objectType = "people", gql = $"primary_name.first_name = {first}" });

        Assert.Contains("Total: 1", listed);
        Assert.Contains(match.Handle, listed);
    }

    [IntegrationFact]
    public async Task ListObjects_FindsByGrampsId()
    {
        var note = await Session.CreateAsync("create_note", new { text = TestNames.Unique("note") });

        var listed = await Session.CallAsync("list_objects", new { objectType = "notes", grampsId = note.GrampsId });

        Assert.Contains("Total: 1", listed);
        Assert.Contains(note.Handle, listed);
    }

    [IntegrationFact]
    public async Task ListObjects_InvalidGql_ReportsGrampsWebError()
    {
        var error = await Session.CallErrorAsync("list_objects", new { objectType = "people", gql = "((" });

        Assert.NotEmpty(error);
    }

    [IntegrationFact]
    public async Task GetObject_UnknownGrampsId_SaysNotFound()
    {
        var text = await Session.CallAsync("get_object", new { identifier = "I9999" });

        Assert.Contains("Person not found: I9999", text);
    }

    [IntegrationFact]
    public async Task RecentChanges_ShowTheLatestEdit()
    {
        var person = await Session.CreateAsync("create_person", new { primaryName = new { first = "Olga", surname = TestNames.Surname() } });

        var changes = await Session.CallAsync("get_recent_changes", new { limit = 1 });

        Assert.Contains("RECENT CHANGES (1 of", changes);
        Assert.Contains("by Integration Owner", changes);
        Assert.Contains(person.Handle, changes);
    }

    [IntegrationFact]
    public async Task Bookmarks_EmptyTree()
    {
        var bookmarks = await Session.CallAsync("get_bookmarks");

        Assert.Contains("No bookmarks in this tree.", bookmarks);
    }

    [IntegrationTheory]
    [InlineData("types", null, "Birth")]
    [InlineData("metadata", null, "Integration tests")]
    [InlineData("name-settings", null, "")]
    [InlineData("input-guide", "dates", "Julian")]
    public async Task Reference_ReadsTopicsFromGrampsWeb(string topic, string? section, string expected)
    {
        var text = await Session.CallAsync("get_reference", new { topic, section });

        Assert.Contains(expected, text);
    }
}
