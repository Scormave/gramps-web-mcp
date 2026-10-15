using Xunit;

namespace GrampsWeb.Mcp.IntegrationTests;

/// <summary>Places, sources, citations, repositories, notes, tags and dates.</summary>
[Collection(GrampsWebCollection.Name)]
public sealed class RecordTests(GrampsWebFixture gramps)
{
    private McpSession Session => gramps.Session;

    [IntegrationFact]
    public async Task Place_IsEnclosedByAnotherPlace()
    {
        var townName = TestNames.Surname();
        var town = await Session.CreateAsync("create_place", new { name = townName, placeType = "Town" });

        var village = await Session.CreateAsync("create_place", new
        {
            name = TestNames.Surname(),
            placeType = "Village",
            enclosedBy = town.Handle,
            lat = "57.6261",
            lon = "39.8845"
        });

        var stored = await Session.CallAsync("get_object", new { identifier = village.GrampsId });
        Assert.Contains("Village", stored);
        Assert.Contains(townName, stored);
        Assert.Contains(town.Handle, stored);
        Assert.Contains("57.6261", stored);
    }

    [IntegrationFact]
    public async Task SourceChain_RepositorySourceCitationEvent()
    {
        var archive = await Session.CreateAsync("create_repository", new { name = TestNames.Surname() + " archive", repoType = "Archive" });
        var title = TestNames.Surname() + " metric book";
        var source = await Session.CreateAsync("create_source", new
        {
            title,
            author = "Church",
            repositoryHandles = new[] { archive.Handle }
        });
        var citation = await Session.CreateAsync("create_citation", new
        {
            sourceHandle = source.Handle,
            page = "f. 12, No. 34",
            date = "1890",
            confidence = "High"
        });
        var birth = await Session.CreateAsync("create_event", new
        {
            eventType = "Birth",
            date = "1890-05-03",
            citationHandles = new[] { citation.Handle }
        });

        var storedCitation = await Session.CallAsync("get_object", new { identifier = citation.GrampsId });
        Assert.Contains($"Source: {title} [handle: {source.Handle}]", storedCitation);
        Assert.Contains("Page/Location: f. 12, No. 34", storedCitation);
        Assert.Contains("Confidence: High", storedCitation);
        Assert.Contains("Date: 1890", storedCitation);

        var storedSource = await Session.CallAsync("get_object", new { identifier = source.GrampsId });
        Assert.Contains(archive.Handle, storedSource);

        var storedEvent = await Session.CallAsync("get_object", new { identifier = birth.GrampsId });
        Assert.Contains(citation.Handle, storedEvent);

        var citations = await Session.CallAsync("list_objects", new { objectType = "citations", sourceHandle = source.Handle });
        Assert.Contains("Total: 1", citations);
        Assert.Contains(citation.Handle, citations);
    }

    [IntegrationFact]
    public async Task UpdateNote_AppendsAndReplacesText()
    {
        var note = await Session.CreateAsync("create_note", new { text = "First line. Fixme here.", noteType = "Research" });

        await Session.CallAsync("update_note", new { handle = note.Handle, appendText = "Second line." });
        await Session.CallAsync("update_note", new { handle = note.Handle, replaceText = "Fixme", withText = "Fixed" });

        var stored = await Session.CallAsync("get_object", new { identifier = note.GrampsId });
        Assert.Contains("Type:   Research", stored);
        Assert.Contains("First line. Fixed here.\nSecond line.", stored.ReplaceLineEndings("\n"));
    }

    [IntegrationFact]
    public async Task UpdateNote_ReplaceTextNotFound_LeavesNoteAlone()
    {
        var note = await Session.CreateAsync("create_note", new { text = "Original text." });

        var error = await Session.CallErrorAsync("update_note", new { handle = note.Handle, replaceText = "Missing", withText = "x" });

        Assert.Contains("replaceText was not found in the note", error);
        var stored = await Session.CallAsync("get_object", new { identifier = note.GrampsId });
        Assert.Contains("Original text.", stored);
    }

    [IntegrationFact]
    public async Task Tag_IsAttachedToPerson()
    {
        var tagName = TestNames.Unique("tag");
        var tag = await Session.CreateAsync("create_tag", new { name = tagName, color = "#ff0000" });
        var person = await Session.CreateAsync("create_person", new { primaryName = new { first = "Anna", surname = TestNames.Surname() } });

        await Session.CallAsync("update_person", new { handle = person.Handle, tagHandles = new[] { tag.Handle }, linkMode = "add" });

        var stored = await Session.CallAsync("get_object", new { identifier = person.GrampsId });
        Assert.Contains(tagName, stored);
    }

    [IntegrationTheory]
    [InlineData("1890-05-03", "1890-05-03")]
    [InlineData("1890-05", "1890-05")]
    [InlineData("about 1884", "about 1884")]
    [InlineData("between 1850 and 1860", "between 1850 and 1860")]
    [InlineData("before 1 Apr 1920", "before 1920-04-01")]
    [InlineData("estimated about 1930-08", "estimated about 1930-08")]
    [InlineData("1856-07-20 (Julian)", "1856-07-20 (Julian)")]
    [InlineData("1735-03-10 (Julian, Mar25)", "1735-03-10 (Julian, Mar25)")]
    public async Task EventDate_IsStoredAndShownAsGrampsWebShowsIt(string date, string shown)
    {
        var description = TestNames.Unique("date");

        var created = await Session.CreateAsync("create_event", new { eventType = "Birth", date, description });

        var stored = await Session.CallAsync("get_object", new { identifier = created.GrampsId });
        Assert.Contains(shown, stored);
    }

    [IntegrationFact]
    public async Task EventDate_ThatDoesNotExist_IsRejected()
    {
        var error = await Session.CallErrorAsync("create_event", new { eventType = "Birth", date = "1900-02-29" });

        Assert.Contains("1900-02-29", error);
    }
}
