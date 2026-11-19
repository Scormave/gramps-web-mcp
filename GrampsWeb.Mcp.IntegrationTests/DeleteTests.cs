using Xunit;

namespace GrampsWeb.Mcp.IntegrationTests;

[Collection(GrampsWebCollection.Name)]
public sealed class DeleteTests(GrampsWebFixture gramps)
{
    private McpSession Session => gramps.Session;

    [IntegrationFact]
    public async Task Delete_UnlinkedRecord()
    {
        var note = await Session.CreateAsync("notes", new { text = TestNames.Unique("note") });

        var deleted = await Session.CallAsync("delete_records", new { identifiers = new[] { note.GrampsId } });

        Assert.Equal("deleted", McpSession.Header(deleted, "action"));
        var gone = await Session.CallAsync("get_records", new { identifiers = new[] { note.Handle }, objectType = "note" });
        Assert.Contains($"Note not found: {note.Handle}", gone);
    }

    [IntegrationFact]
    public async Task Delete_LinkedRecord_IsRefusedUntilForced()
    {
        var birth = await Session.CreateAsync("events", new { eventType = "Birth", date = "1901" });
        var person = await Session.CreateAsync("people", new
        {
            primaryName = new { first = "Ivan", surname = TestNames.Surname() },
            eventRefs = new[] { $"{birth.Handle}::Primary" }
        });

        var refused = await Session.CallAsync("delete_records", new { identifiers = new[] { birth.Handle }, objectType = "event" });
        Assert.Equal("refused", McpSession.Header(refused, "action"));
        Assert.Contains("Nothing was deleted: other records link to it.", refused);
        Assert.Contains("linked from people: 1", refused);
        Assert.Contains(birth.Handle, await Session.CallAsync("get_records", new { identifiers = new[] { person.GrampsId } }));

        var forced = await Session.CallAsync("delete_records", new { identifiers = new[] { birth.Handle }, objectType = "event", force = true });
        Assert.Equal("deleted", McpSession.Header(forced, "action"));
        Assert.Contains("Links to them removed from (1):", forced);
        Assert.Contains(person.Handle, forced);

        var gone = await Session.CallAsync("get_records", new { identifiers = new[] { birth.Handle }, objectType = "event" });
        Assert.Contains("Event not found", gone);
        // The person outlives the deleted event, and Gramps Web removed the link to it.
        var stored = await Session.CallAsync("get_records", new { identifiers = new[] { person.GrampsId } });
        Assert.Contains(person.Handle, stored);
        Assert.DoesNotContain(birth.Handle, stored);
    }

    [IntegrationFact]
    public async Task Delete_ListsTheRecordsLeftWithoutLinks()
    {
        var source = await Session.CreateAsync("sources", new { title = TestNames.Unique("source") });
        var note = await Session.CreateAsync("notes", new { text = TestNames.Unique("transcription") });
        var citation = await Session.CreateAsync("citations", new
        {
            sourceHandle = source.Handle,
            page = "f. 12",
            noteHandles = new[] { note.Handle }
        });
        var birth = await Session.CreateAsync("events", new
        {
            eventType = "Birth",
            date = "1901",
            citationHandles = new[] { citation.Handle }
        });

        var eventDeleted = await Session.CallAsync("delete_records", new { identifiers = new[] { birth.GrampsId } });
        Assert.Equal("deleted", McpSession.Header(eventDeleted, "action"));
        Assert.Contains("Kept, but no record links to it now (1)", eventDeleted);
        Assert.Contains($"handle: {citation.Handle} | gramps_id: {citation.GrampsId}", eventDeleted);
        Assert.DoesNotContain(note.Handle, eventDeleted);
        Assert.DoesNotContain(source.Handle, eventDeleted);

        var citationDeleted = await Session.CallAsync("delete_records", new { identifiers = new[] { citation.GrampsId } });
        Assert.Contains("Kept, but no record links to it now (1)", citationDeleted);
        Assert.Contains($"handle: {note.Handle} | gramps_id: {note.GrampsId}", citationDeleted);
    }
}
