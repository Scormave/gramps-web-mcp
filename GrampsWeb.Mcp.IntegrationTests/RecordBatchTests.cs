using System.Text.RegularExpressions;
using Xunit;

namespace GrampsWeb.Mcp.IntegrationTests;

[Collection(GrampsWebCollection.Name)]
public sealed class RecordBatchTests(GrampsWebFixture gramps)
{
    private McpSession Session => gramps.Session;

    [IntegrationFact]
    public async Task CreateRecords_SavesAFamilyWithItsSourcesInOneTransaction()
    {
        var surname = TestNames.Surname();
        var title = TestNames.Surname() + " metric book";
        var mother = await Session.CreateAsync("create_person", new
        {
            primaryName = new { first = "Maria", surname = TestNames.Surname() },
            gender = "female"
        });

        var result = await Session.CallAsync("create_records", new
        {
            sources = new[] { new { key = "book", title } },
            citations = new[] { new { key = "entry", sourceHandle = "#book", page = "f. 12, No. 34", date = "1890" } },
            events = new[]
            {
                new { key = "birth", eventType = "Birth", date = "1890-05-03", citationHandles = new[] { "#entry" } },
                new { key = "marriage", eventType = "Marriage", date = "about 1884", citationHandles = Array.Empty<string>() }
            },
            people = new object[]
            {
                new { key = "ivan", primaryName = new { first = "Ivan", surname }, gender = "male" },
                new { key = "petr", primaryName = new { first = "Petr", surname }, gender = "male", eventRefs = new[] { "#birth" } }
            },
            families = new[]
            {
                new
                {
                    key = "family",
                    fatherHandle = "#ivan",
                    motherHandle = mother.GrampsId,
                    eventRefs = new[] { "#marriage::Family" },
                    childRefs = new[] { "#petr" }
                }
            }
        });

        Assert.Equal("created", McpSession.Header(result, "action"));
        Assert.Equal("7", McpSession.Header(result, "records"));
        Assert.Contains("Existing records that now link back to a new family (1):", result);
        Assert.Contains($"— handle: {mother.Handle} | gramps_id: {mother.GrampsId}", result);
        Assert.DoesNotContain("Not linked from any record of this call", result);
        var changes = await Session.CallAsync("get_recent_changes", new { limit = 1 });
        Assert.Contains("Add multiple objects", changes);

        var father = Record(result, "ivan");
        var son = Record(result, "petr");
        var family = Record(result, "family");
        var storedFamily = await Session.CallAsync("get_object", new { identifier = family.GrampsId });
        Assert.Contains($"Father: {surname}, Ivan ({father.GrampsId})", storedFamily);
        Assert.Contains($"({mother.GrampsId})", storedFamily);
        Assert.Contains("Marriage: about 1884", storedFamily);
        Assert.Contains("Children (1):", storedFamily);
        Assert.Contains($"({son.GrampsId}) [handle: {son.Handle}], b. 1890-05-03", storedFamily);

        Assert.Contains($"[handle: {family.Handle}]", await Session.CallAsync("get_object", new { identifier = son.GrampsId }));
        Assert.Contains($"[handle: {family.Handle}]", await Session.CallAsync("get_object", new { identifier = mother.GrampsId }));

        var storedBirth = await Session.CallAsync("get_object", new { identifier = Record(result, "birth").GrampsId });
        Assert.Contains(Record(result, "entry").Handle, storedBirth);
        var storedCitation = await Session.CallAsync("get_object", new { identifier = Record(result, "entry").GrampsId });
        Assert.Contains($"Source: {title} [handle: {Record(result, "book").Handle}]", storedCitation);
    }

    [IntegrationFact]
    public async Task Undo_OfCreateRecords_RemovesEveryRecord()
    {
        var surname = TestNames.Surname();
        var result = await Session.CallAsync("create_records", new
        {
            events = new[] { new { key = "birth", eventType = "Birth", date = "1885" } },
            people = new object[]
            {
                new { key = "ivan", primaryName = new { first = "Ivan", surname }, gender = "male" },
                new { key = "anna", primaryName = new { first = "Anna", surname }, gender = "female", eventRefs = new[] { "#birth" } }
            },
            families = new[] { new { key = "family", fatherHandle = "#ivan", childRefs = new[] { "#anna" } } }
        });
        var transaction = await LatestTransactionAsync();

        // The family links both people back in the same transaction, which must not count as a later change.
        var check = await Session.CallAsync("undo_change", new { transactionId = transaction, dryRun = true });
        Assert.Equal("0", McpSession.Header(check, "conflicts"));

        var undone = await Session.CallAsync("undo_change", new { transactionId = transaction });

        Assert.Equal("undone", McpSession.Header(undone, "action"));
        foreach (var key in new[] { "birth", "ivan", "anna", "family" })
        {
            var record = Record(result, key);
            Assert.Contains($"not found: {record.GrampsId}", await Session.CallAsync("get_object", new { identifier = record.GrampsId }));
        }
    }

    [IntegrationFact]
    public async Task CreateRecords_ThatGrampsWebRejects_WritesNothing()
    {
        var surname = TestNames.Surname();
        var before = await LatestTransactionAsync();

        // The family is saved after both people, and Gramps Web fails it on the child that does not exist.
        var error = await Session.CallErrorAsync("create_records", new
        {
            people = new[] { new { key = "ivan", primaryName = new { first = "Ivan", surname }, gender = "male" } },
            families = new[] { new { fatherHandle = "#ivan", childRefs = new[] { "nosuchperson0000" } } }
        });

        Assert.Contains("Gramps Web refused the call and saved none of the records.", error);
        Assert.Equal(before, await LatestTransactionAsync());
        Assert.Contains("No results found", await Session.CallAsync("search", new { query = surname }));
    }

    [IntegrationFact]
    public async Task CreateRecords_WithABadKey_WritesNothing()
    {
        var before = await LatestTransactionAsync();

        var error = await Session.CallErrorAsync("create_records", new
        {
            notes = new[] { new { key = "note", text = TestNames.Unique("note") } },
            people = new[] { new { primaryName = new { first = "Ivan", surname = TestNames.Surname() }, noteHandles = new[] { "#nota" } } }
        });

        Assert.Contains("has \"#nota\", which is not a key of this call. Keys of note records in this call: #note.", error);
        Assert.Equal(before, await LatestTransactionAsync());
    }

    /// <summary>The handle and Gramps ID of the record the call gave <paramref name="key"/>.</summary>
    private static (string Handle, string GrampsId) Record(string result, string key)
    {
        var match = Regex.Match(result, $@"— handle: (\S+) \| gramps_id: (\S+) \| key: {Regex.Escape(key)}\r?$",
            RegexOptions.Multiline);
        Assert.True(match.Success, $"No record with key {key} in:\n{result}");
        return (match.Groups[1].Value, match.Groups[2].Value);
    }

    private async Task<int> LatestTransactionAsync()
    {
        var changes = await Session.CallAsync("get_recent_changes", new { limit = 1 });
        var match = Regex.Match(changes, @"\[transaction: (\d+)\]");
        Assert.True(match.Success, $"No transaction number in:\n{changes}");
        return int.Parse(match.Groups[1].Value);
    }
}
