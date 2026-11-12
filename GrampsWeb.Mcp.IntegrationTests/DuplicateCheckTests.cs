using System.Text.RegularExpressions;
using Xunit;

namespace GrampsWeb.Mcp.IntegrationTests;

/// <summary>
/// The duplicate check of create_records against a real Gramps Web, where its name search runs
/// as regular expressions on the server, and find_duplicates over the whole tree.
/// </summary>
[Collection(GrampsWebCollection.Name)]
public sealed class DuplicateCheckTests(GrampsWebFixture gramps)
{
    private const string Cyrillic = "абвгдежзиклмнопрстуфхцчшэю";

    private McpSession Session => gramps.Session;

    [IntegrationFact]
    public async Task CreateRecords_OfANamesakeBornAYearApart_IsRefused_UntilNotDuplicateOfNamesThem()
    {
        var stem = CyrillicSurname();
        var existing = await CreatePersonAsync("Ѳедоръ", stem + "овъ", "male", "1880", patronymic: "Ивановъ");
        var birth = await Session.CreateAsync("events", new { eventType = "Birth", date = "1881" });
        var before = await LatestTransactionAsync();
        var name = new { first = "Федор", surname = stem + "ов", patronymic = "Иванович" };

        var refused = await Session.CallAsync("create_records", new
        {
            people = new[] { new { primaryName = name, gender = "male", eventRefs = new[] { birth.Handle } } }
        });

        Assert.Equal("refused", McpSession.Header(refused, "action"));
        Assert.Equal("1", McpSession.Header(refused, "duplicates"));
        AssertContains($"{stem}овъ Ивановъ, Ѳедоръ ({existing.GrampsId}) [handle: {existing.Handle}], b. 1880 — " +
                        "same name, born 1 year apart", refused);
        AssertContains($"people[0] notDuplicateOf: [\"{existing.GrampsId}\"]", refused);
        Assert.Equal(before, await LatestTransactionAsync());

        var created = await Session.CreateAsync("people", new
        {
            primaryName = name,
            gender = "male",
            eventRefs = new[] { birth.Handle },
            notDuplicateOf = new[] { existing.GrampsId }
        });

        Assert.Equal("created", created.Action);
        AssertDoesNotContain(existing.GrampsId, created.Text);
    }

    [IntegrationFact]
    public async Task CreateRecords_OfANamesakeBornYearsApart_CreatesThePerson_AndListsTheNamesake()
    {
        var surname = TestNames.Surname() + "ov";
        var existing = await CreatePersonAsync("Anna", surname, "female", "1880");

        var created = await CreatePersonAsync("Anna", surname + "a", "female", "1884");

        Assert.Equal("created", created.Action);
        AssertContains("Similar people already in the tree, possibly the same as a new person:", created.Text);
        AssertContains($"— handle: {created.Handle} (1):", created.Text);
        AssertContains($"({existing.GrampsId}) [handle: {existing.Handle}], b. 1880 — same name, born 4 years apart",
            created.Text);
    }

    [IntegrationFact]
    public async Task CreateRecords_WithAChildTheParentsAlreadyHave_SavesNothing_UntilNotDuplicateOfNamesThem()
    {
        var surname = TestNames.Surname();
        var father = await CreatePersonAsync("Ivan", surname, "male");
        var son = await CreatePersonAsync("Petr", surname, "male");
        await Session.CreateAsync("families", new { fatherHandle = father.Handle, childRefs = new[] { son.Handle } });
        var before = await LatestTransactionAsync();

        object Records(string[]? notDuplicateOf) => new
        {
            people = new[] { new { key = "petr", primaryName = new { first = "Petr", surname }, gender = "male", notDuplicateOf } },
            families = new[] { new { fatherHandle = father.Handle, childRefs = new[] { "#petr" } } }
        };

        var refused = await Session.CallAsync("create_records", Records(null));

        Assert.Equal("refused", McpSession.Header(refused, "action"));
        AssertContains("Created none of the records: the tree may already have one of the new people.", refused);
        AssertContains($"({son.GrampsId}) [handle: {son.Handle}], child of {surname}, Ivan ({father.GrampsId}) — " +
                        "same name and parents, no birth years to compare", refused);
        AssertContains($"people[0] (key petr) notDuplicateOf: [\"{son.GrampsId}\"]", refused);
        Assert.Equal(before, await LatestTransactionAsync());

        var created = await Session.CallAsync("create_records", Records([son.GrampsId]));

        Assert.Equal("created", McpSession.Header(created, "action"));
        Assert.Equal("2", McpSession.Header(created, "records"));
    }

    [IntegrationFact]
    public async Task FindDuplicates_ListsAPersonEnteredTwice_ButNotANamesakeWhoDiedBeforeTheOtherWasBorn()
    {
        var surname = TestNames.Surname() + "ov";
        var existing = await CreatePersonAsync("Anna", surname + "a", "female", "1880");
        var twin = await CreatePersonAsync("Anna", surname, "female", "1881", notDuplicateOf: [existing.GrampsId]);
        var infant = await CreatePersonAsync("Ivan", surname, "male", "1890", deathYear: "1890");

        var namesake = await CreatePersonAsync("Ivan", surname, "male", "1892");
        var pairs = await Session.CallAsync("find_duplicates", new { surname });

        Assert.Equal("created", namesake.Action);
        AssertDoesNotContain(infant.GrampsId, namesake.Text);
        AssertContains("1 possible duplicate and 0 similar pairs; showing 1–1.", pairs);
        AssertContains("1. Same name, born 1 year apart:", pairs);
        AssertContains($"({existing.GrampsId}) [handle: {existing.Handle}], b. 1880", pairs);
        AssertContains($"({twin.GrampsId}) [handle: {twin.Handle}], b. 1881", pairs);
        AssertDoesNotContain(infant.GrampsId, pairs);
        AssertDoesNotContain(namesake.GrampsId, pairs);
    }

    /// <summary>A person with a birth and a death in the years given, and a patronymic when given.</summary>
    private async Task<CreatedRecord> CreatePersonAsync(string first, string surname, string gender,
        string? birthYear = null, string? patronymic = null, string? deathYear = null, string[]? notDuplicateOf = null)
    {
        var eventRefs = new List<string>();
        if (birthYear != null)
            eventRefs.Add((await Session.CreateAsync("events", new { eventType = "Birth", date = birthYear })).Handle);
        if (deathYear != null)
            eventRefs.Add((await Session.CreateAsync("events", new { eventType = "Death", date = deathYear })).Handle);
        return await Session.CreateAsync("people",
            new { primaryName = new { first, surname, patronymic }, gender, eventRefs, notDuplicateOf });
    }

    /// <summary>A capitalised word of random Cyrillic letters.</summary>
    private static string CyrillicSurname()
    {
        var letters = TestNames.Surname().ToLowerInvariant().Select(c => Cyrillic[c - 'a']).ToArray();
        letters[0] = char.ToUpperInvariant(letters[0]);
        return new string(letters);
    }

    /// <summary>
    /// Assert.Contains that shows the whole reply when it fails: xUnit cuts the string to its first 40 characters,
    /// and the reply says why the duplicate check found no one, such as a failed name search.
    /// </summary>
    private static void AssertContains(string expected, string reply) =>
        Assert.True(reply.Contains(expected, StringComparison.Ordinal), $"Not found: {expected}\nin:\n{reply}");

    private static void AssertDoesNotContain(string unexpected, string reply) =>
        Assert.False(reply.Contains(unexpected, StringComparison.Ordinal), $"Found: {unexpected}\nin:\n{reply}");

    private async Task<int> LatestTransactionAsync()
    {
        var changes = await Session.CallAsync("get_recent_changes", new { limit = 1 });
        var match = Regex.Match(changes, @"\[transaction: (\d+)\]");
        Assert.True(match.Success, $"No transaction number in:\n{changes}");
        return int.Parse(match.Groups[1].Value);
    }
}
