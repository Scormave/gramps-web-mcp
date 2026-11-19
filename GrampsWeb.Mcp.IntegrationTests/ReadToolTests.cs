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
        if (!gramps.HasGql)
            return; // API 3.26 removed gql: ListObjects_GqlIsRefusedFromApi326 covers that.

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

    // The gql description of list_objects promises this syntax; these two tests hold Gramps Web to it.
    [IntegrationFact]
    public async Task ListObjects_GqlComparesWithOneEqualsSignAndQuotedText()
    {
        if (!gramps.HasGql)
            return; // API 3.26 removed gql: ListObjects_GqlIsRefusedFromApi326 covers that.

        var first = TestNames.Surname();
        var man = await Session.CreateAsync("create_person", new { primaryName = new { first, surname = TestNames.Surname() }, gender = "male" });
        await Session.CreateAsync("create_person", new { primaryName = new { first, surname = TestNames.Surname() }, gender = "female" });

        var listed = await Session.CallAsync("list_objects", new { objectType = "people", gql = $"gender = 1 and primary_name.first_name = \"{first}\"" });
        var error = await Session.CallErrorAsync("list_objects", new { objectType = "people", gql = $"gender == 1 and primary_name.first_name = \"{first}\"" });

        Assert.Contains("Total: 1", listed);
        Assert.Contains(man.Handle, listed);
        Assert.Contains("Gramps API error 422", error);
    }

    [IntegrationFact]
    public async Task ListObjects_GqlMatchesEventTypesByNumber()
    {
        if (!gramps.HasGql)
            return; // API 3.26 removed gql: ListObjects_GqlIsRefusedFromApi326 covers that.

        var description = TestNames.Unique("birth");
        var birth = await Session.CreateAsync("create_event", new { eventType = "Birth", date = "1901", description });

        var byNumber = await Session.CallAsync("list_objects", new { objectType = "events", gql = $"type.value = 12 and description = \"{description}\"" });
        var byName = await Session.CallAsync("list_objects", new { objectType = "events", gql = $"type = \"Birth\" and description = \"{description}\"" });

        Assert.Contains("Total: 1", byNumber);
        Assert.Contains(birth.Handle, byNumber);
        Assert.Equal("No events found.", byName);
    }

    [IntegrationFact]
    public async Task ListObjects_GqlIsRefusedFromApi326()
    {
        if (gramps.HasGql)
            return;

        var error = await Session.CallErrorAsync("list_objects", new { objectType = "people", gql = "gender = 1" });

        Assert.Contains("no longer has Gramps QL", error);
        Assert.Contains("MatchesQuery", error);
    }

    // The rules description of list_objects promises this GOQL syntax; these two tests hold Gramps Web to it.
    [IntegrationFact]
    public async Task ListObjects_MatchesQueryComparesWithTwoEqualsSignsQuotedTextAndLen()
    {
        var first = TestNames.Surname();
        var birth = await Session.CreateAsync("create_event", new { eventType = "Birth", date = "1901" });
        var born = await Session.CreateAsync("create_person", new
        {
            primaryName = new { first, surname = TestNames.Surname() },
            eventRefs = new[] { $"{birth.Handle}::Primary" }
        });
        await Session.CreateAsync("create_person", new { primaryName = new { first, surname = TestNames.Surname() } });

        var listed = await Session.CallAsync("list_objects", new
        {
            objectType = "people",
            rules = new[] { new { name = "MatchesQuery", values = new[] { $"primary_name.first_name == '{first}' and len(event_ref_list) >= 1" } } }
        });

        Assert.Contains("Total: 1", listed);
        Assert.Contains(born.Handle, listed);
    }

    [IntegrationFact]
    public async Task ListObjects_MatchesQueryMatchesEventTypesByConstant()
    {
        var description = TestNames.Unique("birth");
        var birth = await Session.CreateAsync("create_event", new { eventType = "Birth", date = "1901", description });
        await Session.CreateAsync("create_event", new { eventType = "Residence", date = "1901", description });

        var listed = await Session.CallAsync("list_objects", new
        {
            objectType = "events",
            rules = new[] { new { name = "MatchesQuery", values = new[] { $"type.value == EventType.BIRTH and description == '{description}'" } } }
        });

        Assert.Contains("Total: 1", listed);
        Assert.Contains(birth.Handle, listed);
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
