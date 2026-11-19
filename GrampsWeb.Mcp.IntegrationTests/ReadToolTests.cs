using Xunit;

namespace GrampsWeb.Mcp.IntegrationTests;

/// <summary>search, list_records, get_records, get_recent_changes, get_bookmarks and get_reference.</summary>
[Collection(GrampsWebCollection.Name)]
public sealed class ReadToolTests(GrampsWebFixture gramps)
{
    private McpSession Session => gramps.Session;

    [IntegrationFact]
    public async Task Search_FindsPersonBySurname()
    {
        var surname = TestNames.Surname();
        var person = await Session.CreateAsync("people", new { primaryName = new { first = "Ivan", surname } });

        var found = await Session.CallAsync("search", new { query = surname });

        Assert.Contains("Total: 1", found);
        Assert.Contains($"Person: {surname}, Ivan — handle: {person.Handle} | gramps_id: {person.GrampsId}", found);
    }

    [IntegrationFact]
    public async Task ListRecords_FindsByGrampsId()
    {
        var note = await Session.CreateAsync("notes", new { text = TestNames.Unique("note") });

        var listed = await Session.CallAsync("list_records", new { objectType = "notes", grampsId = note.GrampsId });

        Assert.Contains("Total: 1", listed);
        Assert.Contains(note.Handle, listed);
    }

    // The query description of list_records promises this GOQL syntax; these tests hold Gramps Web to it.
    [IntegrationFact]
    public async Task ListRecords_QueryComparesWithTwoEqualsSignsQuotedTextAndLen()
    {
        var first = TestNames.Surname();
        var birth = await Session.CreateAsync("events", new { eventType = "Birth", date = "1901" });
        var born = await Session.CreateAsync("people", new
        {
            primaryName = new { first, surname = TestNames.Surname() },
            eventRefs = new[] { $"{birth.Handle}::Primary" }
        });
        await Session.CreateAsync("people", new { primaryName = new { first, surname = TestNames.Surname() } });

        var listed = await Session.CallAsync("list_records", new
        {
            objectType = "people",
            query = $"primary_name.first_name == '{first}' and len(event_ref_list) >= 1"
        });
        var dated = await Session.CallAsync("list_records", new
        {
            objectType = "people",
            query = $"primary_name.first_name == '{first}' and birth.date.sortval > 0"
        });

        Assert.Contains("Total: 1", listed);
        Assert.Contains(born.Handle, listed);
        Assert.Contains("Total: 1", dated);
        Assert.Contains(born.Handle, dated);
    }

    [IntegrationFact]
    public async Task ListRecords_QueryMatchesEventTypesByConstant()
    {
        var description = TestNames.Unique("birth");
        var birth = await Session.CreateAsync("events", new { eventType = "Birth", date = "1901", description });
        await Session.CreateAsync("events", new { eventType = "Residence", date = "1901", description });

        var listed = await Session.CallAsync("list_records", new
        {
            objectType = "events",
            query = $"type.value == EventType.BIRTH and description == '{description}'"
        });

        Assert.Contains("Total: 1", listed);
        Assert.Contains(birth.Handle, listed);
    }

    [IntegrationFact]
    public async Task ListRecords_QueryMatchesListItemsWithAny_AndMustMatchTheRulesToo()
    {
        var surname = TestNames.Surname();
        var woman = await Session.CreateAsync("people", new { primaryName = new { first = "Anna", surname }, gender = "female" });
        await Session.CreateAsync("people", new { primaryName = new { first = "Ivan", surname }, gender = "male" });

        var both = await Session.CallAsync("list_records", new
        {
            objectType = "people",
            query = $"any(primary_name.surname_list, surname == '{surname}')"
        });
        var women = await Session.CallAsync("list_records", new
        {
            objectType = "people",
            query = $"any(primary_name.surname_list, surname == '{surname}') and gender == Person.FEMALE"
        });
        var withRules = await Session.CallAsync("list_records", new
        {
            objectType = "people",
            query = $"any(primary_name.surname_list, surname == '{surname}')",
            rules = new[] { new { name = "IsFemale" } }
        });

        Assert.Contains("Total: 2", both);
        Assert.Contains("Total: 1", women);
        Assert.Contains(woman.Handle, women);
        Assert.Contains("Total: 1", withRules);
        Assert.Contains(woman.Handle, withRules);
    }

    [IntegrationFact]
    public async Task ListRecords_InvalidQuery_GivesGrampsWebsReason()
    {
        var syntax = await Session.CallErrorAsync("list_records", new { objectType = "people", query = "gender = 1" });
        var field = await Session.CallErrorAsync("list_records", new { objectType = "people", query = "first_name == 'Anna'" });

        Assert.Contains("Gramps Web could not run the GOQL query: invalid syntax", syntax);
        Assert.Contains("== for equals", syntax);
        Assert.Contains("Gramps Web could not run the GOQL query: unknown field 'first_name' on Person", field);
        Assert.Contains("primary_name", field);
    }

    [IntegrationFact]
    public async Task ListRecords_TheOldGqlArgument_PointsToQuery()
    {
        var error = await Session.CallErrorAsync("list_records", new { objectType = "people", gql = "gender = 1" });

        Assert.Contains("gql (did you mean query?)", error);
    }

    [IntegrationFact]
    public async Task GetRecords_UnknownGrampsId_SaysNotFound()
    {
        var text = await Session.CallAsync("get_records", new { identifiers = new[] { "I9999" } });

        Assert.Contains("Person not found: I9999", text);
    }

    [IntegrationFact]
    public async Task GetRecords_ReadsSeveralRecordsInTheOrderGiven()
    {
        var note = await Session.CreateAsync("notes", new { text = TestNames.Unique("note") });
        var person = await Session.CreateAsync("people", new { primaryName = new { first = "Olga", surname = TestNames.Surname() } });

        var text = await Session.CallAsync("get_records",
            new { identifiers = new[] { note.GrampsId, person.GrampsId, "I9999" } });

        var records = text.Split("\n\n---\n\n");
        Assert.Equal(3, records.Length);
        Assert.Contains(note.Handle, records[0]);
        Assert.Contains(person.Handle, records[1]);
        Assert.Contains("Person not found: I9999", records[2]);
    }

    [IntegrationFact]
    public async Task RecentChanges_ShowTheLatestEdit()
    {
        var person = await Session.CreateAsync("people", new { primaryName = new { first = "Olga", surname = TestNames.Surname() } });

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
