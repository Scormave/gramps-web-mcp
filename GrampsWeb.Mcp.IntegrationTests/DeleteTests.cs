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
        var gone = await Session.CallAsync("get_object", new { identifier = note.Handle, objectType = "note" });
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
        Assert.Contains(birth.Handle, await Session.CallAsync("get_object", new { identifier = person.GrampsId }));

        var forced = await Session.CallAsync("delete_records", new { identifiers = new[] { birth.Handle }, objectType = "event", force = true });
        Assert.Equal("deleted", McpSession.Header(forced, "action"));
        Assert.Contains("Links to them removed from (1):", forced);
        Assert.Contains(person.Handle, forced);

        var gone = await Session.CallAsync("get_object", new { identifier = birth.Handle, objectType = "event" });
        Assert.Contains("Event not found", gone);
        // The person outlives the deleted event, and Gramps Web removed the link to it.
        var stored = await Session.CallAsync("get_object", new { identifier = person.GrampsId });
        Assert.Contains(person.Handle, stored);
        Assert.DoesNotContain(birth.Handle, stored);
    }
}
