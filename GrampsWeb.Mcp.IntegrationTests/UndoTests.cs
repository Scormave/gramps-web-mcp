using System.Text.RegularExpressions;
using Xunit;

namespace GrampsWeb.Mcp.IntegrationTests;

[Collection(GrampsWebCollection.Name)]
public sealed class UndoTests(GrampsWebFixture gramps)
{
    private McpSession Session => gramps.Session;

    [IntegrationFact]
    public async Task Undo_OfACreate_DeletesTheRecord()
    {
        var note = await Session.CreateAsync("create_note", new { text = TestNames.Unique("note") });
        var transaction = await LatestTransactionAsync();

        var check = await Session.CallAsync("undo_change", new { transactionId = transaction, dryRun = true });
        Assert.Equal("checked", McpSession.Header(check, "action"));
        Assert.Contains($"Delete Note: ", check);
        Assert.Contains($"[handle: {note.Handle}]", check);
        Assert.DoesNotContain("Note not found", await GetNoteAsync(note.Handle));

        var undone = await Session.CallAsync("undo_change", new { transactionId = transaction });

        Assert.Equal("undone", McpSession.Header(undone, "action"));
        Assert.Equal(await LatestTransactionAsync(), int.Parse(McpSession.Header(undone, "undo_transaction")));
        Assert.Contains($"Note not found: {note.Handle}", await GetNoteAsync(note.Handle));
    }

    [IntegrationFact]
    public async Task Undo_OfAnUpdate_RestoresTheEarlierVersion()
    {
        var original = TestNames.Unique("original");
        var edited = TestNames.Unique("edited");
        var note = await Session.CreateAsync("create_note", new { text = original });
        await Session.CallAsync("update_note", new { handle = note.Handle, text = edited });
        var transaction = await LatestTransactionAsync();

        var undone = await Session.CallAsync("undo_change", new { transactionId = transaction });

        Assert.Equal("undone", McpSession.Header(undone, "action"));
        Assert.Contains($"Restored the earlier version of Note: ", undone);
        var stored = await GetNoteAsync(note.Handle);
        Assert.Contains(original, stored);
        Assert.DoesNotContain(edited, stored);
    }

    [IntegrationFact]
    public async Task Undo_OfADeletion_RestoresTheRecord_WithForceBeforeGrampsWeb323()
    {
        var text = TestNames.Unique("note");
        var note = await Session.CreateAsync("create_note", new { text });
        await Session.CallAsync("delete_object", new { objectType = "note", handle = note.Handle });
        var transaction = await LatestTransactionAsync();

        var result = await Session.CallAsync("undo_change", new { transactionId = transaction });

        Assert.Contains($"the deleted Note {note.GrampsId}: ", result);
        if (ImageBeforeDeletionFix())
        {
            Assert.Equal("refused", McpSession.Header(result, "action"));
            Assert.Contains("restores deleted records only with force=true", result);
            Assert.Contains($"Note not found: {note.Handle}", await GetNoteAsync(note.Handle));
            result = await Session.CallAsync("undo_change", new { transactionId = transaction, force = true });
        }

        Assert.Equal("undone", McpSession.Header(result, "action"));
        var restored = await Session.CallAsync("get_object", new { identifier = note.GrampsId });
        Assert.Contains(note.Handle, restored);
        Assert.Contains(text, restored);
    }

    [IntegrationFact]
    public async Task Undo_IsRefused_AfterALaterEdit()
    {
        var note = await Session.CreateAsync("create_note", new { text = TestNames.Unique("note") });
        var transaction = await LatestTransactionAsync();
        await Session.CallAsync("update_note", new { handle = note.Handle, appendText = " more" });

        var refused = await Session.CallAsync("undo_change", new { transactionId = transaction });

        Assert.Equal("refused", McpSession.Header(refused, "action"));
        Assert.Equal("1", McpSession.Header(refused, "conflicts"));
        Assert.Contains("was changed or deleted after this transaction added it", refused);
        Assert.DoesNotContain("Note not found", await GetNoteAsync(note.Handle));
    }

    [IntegrationFact]
    public async Task Undo_IsRefused_WhenARecordToDeleteIsNowLinked()
    {
        var note = await Session.CreateAsync("create_note", new { text = TestNames.Unique("note") });
        var transaction = await LatestTransactionAsync();
        var person = await Session.CreateAsync("create_person", new
        {
            primaryName = new { first = "Anna", surname = TestNames.Surname() },
            noteHandles = new[] { note.Handle }
        });

        var refused = await Session.CallAsync("undo_change", new { transactionId = transaction });

        Assert.Equal("refused", McpSession.Header(refused, "action"));
        Assert.Contains("is now linked from Person: ", refused);
        Assert.Contains($"[handle: {person.Handle}], which this transaction did not change", refused);
        Assert.DoesNotContain("Note not found", await GetNoteAsync(note.Handle));
    }

    [IntegrationFact]
    public async Task Undo_IsRefused_WhenTheEarlierVersionLinksADeletedRecord()
    {
        var note = await Session.CreateAsync("create_note", new { text = TestNames.Unique("note") });
        var person = await Session.CreateAsync("create_person", new
        {
            primaryName = new { first = "Pyotr", surname = TestNames.Surname() },
            noteHandles = new[] { note.Handle }
        });
        await Session.CallAsync("update_person", new { handle = person.Handle, noteHandles = Array.Empty<string>() });
        var transaction = await LatestTransactionAsync();
        await Session.CallAsync("delete_object", new { objectType = "note", handle = note.Handle });

        var refused = await Session.CallAsync("undo_change", new { transactionId = transaction });

        Assert.Equal("refused", McpSession.Header(refused, "action"));
        Assert.Contains($"to restore links Note [handle: {note.Handle}], which no longer exist", refused);
        Assert.DoesNotContain(note.Handle, await Session.CallAsync("get_object", new { identifier = person.GrampsId }));
    }

    private Task<string> GetNoteAsync(string handle) =>
        Session.CallAsync("get_object", new { identifier = handle, objectType = "note" });

    private async Task<int> LatestTransactionAsync()
    {
        var changes = await Session.CallAsync("get_recent_changes", new { limit = 1 });
        var match = Regex.Match(changes, @"\[transaction: (\d+)\]");
        Assert.True(match.Success, $"No transaction number in:\n{changes}");
        return int.Parse(match.Groups[1].Value);
    }

    /// <summary>Gramps Web images before 26.10 (API 3.23) undo a deletion only with force.</summary>
    private static bool ImageBeforeDeletionFix()
    {
        var tag = GrampsWebImage.Name.Split(':').LastOrDefault() ?? "";
        return Version.TryParse(tag, out var version) && version < new Version(26, 10);
    }
}
