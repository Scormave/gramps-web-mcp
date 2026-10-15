using Xunit;

namespace GrampsWeb.Mcp.IntegrationTests;

[Collection(GrampsWebCollection.Name)]
public sealed class DeleteTests(GrampsWebFixture gramps)
{
    private McpSession Session => gramps.Session;

    [IntegrationFact]
    public async Task Delete_UnlinkedRecord()
    {
        var note = await Session.CreateAsync("create_note", new { text = TestNames.Unique("note") });

        var deleted = await Session.CallAsync("delete_object", new { objectType = "note", handle = note.Handle });

        Assert.Equal("deleted", McpSession.Header(deleted, "action"));
        var gone = await Session.CallAsync("get_object", new { identifier = note.Handle, objectType = "note" });
        Assert.Contains($"Note not found: {note.Handle}", gone);
    }

    [IntegrationFact]
    public async Task Delete_LinkedRecord_IsRefusedUntilForced()
    {
        var birth = await Session.CreateAsync("create_event", new { eventType = "Birth", date = "1901" });
        var person = await Session.CreateAsync("create_person", new
        {
            primaryName = new { first = "Ivan", surname = TestNames.Surname() },
            eventRefs = new[] { $"{birth.Handle}::Primary" }
        });

        var refused = await Session.CallAsync("delete_object", new { objectType = "event", handle = birth.Handle });
        Assert.Contains("other objects reference it", refused);
        Assert.Contains("person: 1 reference(s)", refused);
        Assert.Contains(birth.Handle, await Session.CallAsync("get_object", new { identifier = person.GrampsId }));

        var forced = await Session.CallAsync("delete_object", new { objectType = "event", handle = birth.Handle, force = true });
        Assert.Equal("deleted", McpSession.Header(forced, "action"));

        var gone = await Session.CallAsync("get_object", new { identifier = birth.Handle, objectType = "event" });
        Assert.Contains("Event not found", gone);
        // The person outlives the deleted link.
        var stored = await Session.CallAsync("get_object", new { identifier = person.GrampsId });
        Assert.Contains(person.Handle, stored);
    }
}
