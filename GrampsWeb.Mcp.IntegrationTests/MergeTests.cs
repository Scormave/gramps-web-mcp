using Xunit;

namespace GrampsWeb.Mcp.IntegrationTests;

/// <summary>
/// merge_objects against a real Gramps Web. The test server has no confirmation dialog, so each merge runs
/// without a question.
/// </summary>
[Collection(GrampsWebCollection.Name)]
public sealed class MergeTests(GrampsWebFixture gramps)
{
    private McpSession Session => gramps.Session;

    [IntegrationFact]
    public async Task Merge_OfTwoPeople_MovesTheLinks_AndUndoRestoresTheDuplicate()
    {
        var surname = TestNames.Surname();
        var keep = await Session.CreateAsync("people", new
        {
            primaryName = new { first = "Anna", surname },
            gender = "female"
        });
        var birth = await Session.CreateAsync("events", new { eventType = "Birth", date = "1890" });
        var duplicate = await Session.CreateAsync("people", new
        {
            primaryName = new { first = "Anya", surname },
            gender = "female",
            eventRefs = new[] { $"{birth.Handle}::Primary" }
        });

        var merged = await Session.CallAsync("merge_objects", new
        {
            objectType = "person",
            keepHandle = keep.Handle,
            duplicateHandle = duplicate.Handle
        });

        Assert.Equal("merged", McpSession.Header(merged, "action"));
        Assert.Equal(keep.Handle, McpSession.Header(merged, "handle"));
        Assert.Equal(duplicate.GrampsId, McpSession.Header(merged, "duplicate_gramps_id"));
        var transaction = int.Parse(McpSession.Header(merged, "transaction"));
        var changes = await Session.CallAsync("get_recent_changes", new { limit = 1 });
        Assert.Contains("Merge Person", changes);
        Assert.Contains($"[transaction: {transaction}]", changes);
        var stored = await GetPersonAsync(keep.Handle);
        Assert.Contains($"Primary name [Birth Name]: Anna {surname}", stored);
        Assert.Contains($"[Birth Name] Anya {surname}", stored);
        Assert.Contains($"Merged Gramps ID: {duplicate.GrampsId}", stored);
        Assert.Contains($"Birth: 1890 [Primary] [handle: {birth.Handle}]", stored);
        Assert.Contains($"Person not found: {duplicate.Handle}", await GetPersonAsync(duplicate.Handle));

        var undone = await Session.CallAsync("undo_change", new { transactionId = transaction });
        if (UndoTests.ImageBeforeDeletionFix())
        {
            Assert.Equal("refused", McpSession.Header(undone, "action"));
            undone = await Session.CallAsync("undo_change", new { transactionId = transaction, force = true });
        }

        Assert.Equal("undone", McpSession.Header(undone, "action"));
        Assert.Contains($"Restored the deleted Person {duplicate.GrampsId}: ", undone);
        var restored = await GetPersonAsync(duplicate.Handle);
        Assert.Contains($"Primary name [Birth Name]: Anya {surname}", restored);
        Assert.Contains(birth.Handle, restored);
        Assert.DoesNotContain("Merged Gramps ID", await GetPersonAsync(keep.Handle));
    }

    [IntegrationFact]
    public async Task Merge_OfSpouses_IsRefused_AndChangesNothing()
    {
        var surname = TestNames.Surname();
        var husband = await Session.CreateAsync("people", new
        {
            primaryName = new { first = "Ivan", surname },
            gender = "male"
        });
        var wife = await Session.CreateAsync("people", new
        {
            primaryName = new { first = "Anna", surname },
            gender = "female"
        });
        await Session.CreateAsync("families", new { fatherHandle = husband.Handle, motherHandle = wife.Handle });

        var refused = await Session.CallAsync("merge_objects", new
        {
            objectType = "person",
            keepHandle = husband.Handle,
            duplicateHandle = wife.Handle
        });

        Assert.Equal("refused", McpSession.Header(refused, "action"));
        Assert.Contains("spouses cannot be merged", refused);
        Assert.Contains(wife.Handle, await GetPersonAsync(wife.Handle));
        Assert.DoesNotContain("Merged Gramps ID", await GetPersonAsync(husband.Handle));
    }

    [IntegrationFact]
    public async Task Merge_OfTwoFamilies_JoinsTheFathers_AndMovesTheChildren()
    {
        var surname = TestNames.Surname();
        var father = await Session.CreateAsync("people", new
        {
            primaryName = new { first = "Ivan", surname },
            gender = "male"
        });
        var mother = await Session.CreateAsync("people", new
        {
            primaryName = new { first = "Anna", surname },
            gender = "female"
        });
        var keep = await Session.CreateAsync("families", new { fatherHandle = father.Handle, motherHandle = mother.Handle });
        var otherFather = await Session.CreateAsync("people", new
        {
            primaryName = new { first = "Ioann", surname },
            gender = "male"
        });
        var son = await Session.CreateAsync("people", new
        {
            primaryName = new { first = "Petr", surname },
            gender = "male"
        });
        var duplicate = await Session.CreateAsync("families", new
        {
            fatherHandle = otherFather.Handle,
            relationshipType = "Married",
            childRefs = new[] { $"{son.Handle}::Birth" }
        });

        var merged = await Session.CallAsync("merge_objects", new
        {
            objectType = "family",
            keepHandle = keep.Handle,
            duplicateHandle = duplicate.Handle
        });

        Assert.Equal("merged", McpSession.Header(merged, "action"));
        Assert.Contains("The same merge also joined and deleted 1 person.", merged);
        var family = await Session.CallAsync("get_object", new { objectType = "family", identifier = keep.Handle, extended = true });
        Assert.Contains($"[handle: {father.Handle}]", family);
        Assert.Contains($"[handle: {mother.Handle}]", family);
        Assert.Contains($"[handle: {son.Handle}]", family);
        // The kept family's type was Unknown, so it takes the duplicate's.
        Assert.Contains("Relationship: Married", family);
        Assert.Contains($"Family not found: {duplicate.Handle}",
            await Session.CallAsync("get_object", new { objectType = "family", identifier = duplicate.Handle }));
        Assert.Contains($"Person not found: {otherFather.Handle}", await GetPersonAsync(otherFather.Handle));
        Assert.Contains($"Merged Gramps ID: {otherFather.GrampsId}", await GetPersonAsync(father.Handle));
    }

    [IntegrationFact]
    public async Task Merge_OfTwoNotes_KeepsTheKeptText_AndMovesTheLinks()
    {
        var keptText = TestNames.Unique("kept");
        var droppedText = TestNames.Unique("dropped");
        var keep = await Session.CreateAsync("notes", new { text = keptText });
        var duplicate = await Session.CreateAsync("notes", new { text = droppedText });
        var person = await Session.CreateAsync("people", new
        {
            primaryName = new { first = "Olga", surname = TestNames.Surname() },
            noteHandles = new[] { duplicate.Handle }
        });

        var merged = await Session.CallAsync("merge_objects", new
        {
            objectType = "note",
            keepHandle = keep.GrampsId,
            duplicateHandle = duplicate.GrampsId
        });

        Assert.Equal("merged", McpSession.Header(merged, "action"));
        Assert.Equal(keep.Handle, McpSession.Header(merged, "handle"));
        Assert.Equal(duplicate.Handle, McpSession.Header(merged, "duplicate_handle"));
        var note = await Session.CallAsync("get_object", new { objectType = "note", identifier = keep.Handle });
        Assert.Contains(keptText, note);
        Assert.DoesNotContain(droppedText, note);
        Assert.Contains($"[handle: {person.Handle}]", note);
        Assert.Contains($"Note not found: {duplicate.Handle}",
            await Session.CallAsync("get_object", new { objectType = "note", identifier = duplicate.Handle }));
    }

    private Task<string> GetPersonAsync(string handle) =>
        Session.CallAsync("get_object", new { objectType = "person", identifier = handle, extended = true });
}
