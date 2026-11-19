using Xunit;

namespace GrampsWeb.Mcp.IntegrationTests;

[Collection(GrampsWebCollection.Name)]
public sealed class PeopleAndFamilyTests(GrampsWebFixture gramps)
{
    private McpSession Session => gramps.Session;

    [IntegrationFact]
    public async Task CreatePerson_StoresNamePartsAndGender()
    {
        var surname = TestNames.Surname();

        var person = await Session.CreateAsync("people", new
        {
            primaryName = new { first = "Petr", surname, patronymic = "Ivanovich" },
            gender = "male"
        });

        Assert.Equal("created", person.Action);
        Assert.Matches("^I[0-9]{4}$", person.GrampsId);
        var stored = await Session.CallAsync("get_records", new { identifiers = new[] { person.GrampsId } });
        Assert.Contains($"[handle: {person.Handle}]", stored);
        Assert.Contains("Gender:    Male", stored);
        Assert.Contains("first:   Petr", stored);
        Assert.Contains($"surname: {surname} [primary]", stored);
        Assert.Contains("surname: Ivanovich [Patronymic]", stored);
    }

    [IntegrationFact]
    public async Task UpdatePerson_AddsAndRemovesOneEventLink()
    {
        var person = await CreatePersonAsync("Ivan", TestNames.Surname(), "male");
        var birth = await Session.CreateAsync("events", new { eventType = "Birth", date = "1890-05-03" });
        var baptism = await Session.CreateAsync("events", new { eventType = "Baptism", date = "1890-05-05" });

        await Session.CallAsync("update_person", new
        {
            identifier = person.Handle,
            eventRefs = new object[] { new { @ref = birth.Handle, role = "Primary" }, $"{baptism.Handle}::Primary" },
            linkMode = "add"
        });
        var linked = await Session.CallAsync("get_records", new { identifiers = new[] { person.Handle }, objectType = "person" });
        Assert.Contains(birth.Handle, linked);
        Assert.Contains(baptism.Handle, linked);

        await Session.CallAsync("update_person", new
        {
            identifier = person.Handle,
            eventRefs = new[] { baptism.Handle },
            linkMode = "remove"
        });
        var unlinked = await Session.CallAsync("get_records", new { identifiers = new[] { person.Handle }, objectType = "person" });
        Assert.Contains(birth.Handle, unlinked);
        Assert.DoesNotContain(baptism.Handle, unlinked);
    }

    [IntegrationFact]
    public async Task CreateFamily_LinksParentsChildrenAndMarriage()
    {
        var family = await CreateFamilyAsync();

        var stored = await Session.CallAsync("get_records", new { identifiers = new[] { family.Family.GrampsId } });
        Assert.Contains($"Father: {family.Surname}, Ivan ({family.Father.GrampsId})", stored);
        Assert.Contains($"Mother: {family.MotherSurname}, Maria ({family.Mother.GrampsId})", stored);
        Assert.Contains("Relationship: Married", stored);
        Assert.Contains("Marriage: about 1884", stored);
        Assert.Contains("Children (2):", stored);
        Assert.Contains($"({family.Son.GrampsId}) [handle: {family.Son.Handle}], b. 1890-05-03", stored);

        var son = await Session.CallAsync("get_records", new { identifiers = new[] { family.Son.GrampsId } });
        Assert.Contains($"[handle: {family.Family.Handle}]", son);
    }

    [IntegrationFact]
    public async Task ChildLink_CarriesCitationsAndNotes()
    {
        var child = await CreatePersonAsync("Petr", TestNames.Surname(), "male");
        var citation = await CreateCitationAsync("No. 7");
        var note = await Session.CreateAsync("notes", new { text = TestNames.Unique("adoption") });

        var family = await Session.CreateAsync("families", new
        {
            relationshipType = "Married",
            childRefs = new object[]
            {
                new { @ref = child.Handle, frel = "Adopted", mrel = "Adopted", citation_list = new[] { citation.GrampsId }, note_list = new[] { note.GrampsId } }
            }
        });

        var stored = await Session.CallAsync("get_records", new { identifiers = new[] { family.GrampsId } });
        Assert.Matches($@"Citations on link: .*\[handle: {citation.Handle}\]", stored);
        Assert.Matches($@"Notes on link: .*\[handle: {note.Handle}\]", stored);

        // Replacing the child list keeps the link's citations and notes unless the link sends its own.
        await Session.CallAsync("update_family", new
        {
            identifier = family.Handle,
            childRefs = new object[] { new { @ref = child.Handle, frel = "Adopted", mrel = "Birth", note_list = Array.Empty<string>() } },
            linkMode = "replace"
        });
        var replaced = await Session.CallAsync("get_records", new { identifiers = new[] { family.GrampsId } });
        Assert.Contains("frel: Adopted, mrel: Birth", replaced);
        Assert.Matches($@"Citations on link: .*\[handle: {citation.Handle}\]", replaced);
        Assert.DoesNotContain("Notes on link:", replaced);
    }

    [IntegrationFact]
    public async Task Names_CarryCitationsThroughUpdates()
    {
        var surname = TestNames.Surname();
        var primary = await CreateCitationAsync("f. 12");
        var alias = await CreateCitationAsync("f. 13");
        var person = await Session.CreateAsync("people", new
        {
            primaryName = new { first = "Ivan", surname, citation_list = new[] { primary.GrampsId } },
            alternateNames = new object[] { new { type = "Also Known As", first = "John", surname, citation_list = new[] { alias.Handle } } },
            gender = "male"
        });

        await Session.CallAsync("update_person", new
        {
            identifier = person.Handle,
            primaryName = new { first = "Ivan", surname, title = "Dr." },
            alternateNames = new object[] { new { type = "Also Known As", first = "John", surname, nick = "Jack" } }
        });

        var stored = await Session.CallAsync("get_records", new { identifiers = new[] { person.GrampsId } });
        Assert.Contains("title:   Dr.", stored);
        Assert.Contains("nick:    Jack", stored);
        Assert.Matches($@"citations: .*\[handle: {primary.Handle}\]", stored);
        Assert.Matches($@"citations: .*\[handle: {alias.Handle}\]", stored);
    }

    private async Task<CreatedRecord> CreateCitationAsync(string page)
    {
        var source = await Session.CreateAsync("sources", new { title = TestNames.Unique("register") });
        return await Session.CreateAsync("citations", new { sourceHandle = source.Handle, page });
    }

    [IntegrationFact]
    public async Task UpdateFamily_SortsChildrenByBirth()
    {
        // The son was born in 1890 and added first, the daughter in 1885.
        var family = await CreateFamilyAsync();
        var before = await Session.CallAsync("get_records", new { identifiers = new[] { family.Family.GrampsId } });
        Assert.True(before.IndexOf(family.Son.Handle, StringComparison.Ordinal)
                    < before.IndexOf(family.Daughter.Handle, StringComparison.Ordinal));

        await Session.CallAsync("update_family", new { identifier = family.Family.Handle, sort = new[] { "Children" } });

        var after = await Session.CallAsync("get_records", new { identifiers = new[] { family.Family.GrampsId } });
        Assert.True(after.IndexOf(family.Daughter.Handle, StringComparison.Ordinal)
                    < after.IndexOf(family.Son.Handle, StringComparison.Ordinal), after);
    }

    [IntegrationFact]
    public async Task PersonTree_ShowsAncestorsAndDescendants()
    {
        var family = await CreateFamilyAsync();

        var ancestors = await Session.CallAsync("get_person_tree", new { person = family.Son.GrampsId, direction = "ancestors" });
        Assert.Contains("Total: 2", ancestors);
        Assert.Contains($"Gen 1 — Father\n     {family.Surname}, Ivan [{family.Father.GrampsId}]", ancestors.ReplaceLineEndings("\n"));
        Assert.Contains($"Gen 1 — Mother\n     {family.MotherSurname}, Maria [{family.Mother.GrampsId}]", ancestors.ReplaceLineEndings("\n"));

        var descendants = await Session.CallAsync("get_person_tree", new { person = family.Father.GrampsId, direction = "descendants" });
        Assert.Contains("Total: 2", descendants);
        Assert.Contains($"[{family.Son.GrampsId}]", descendants);
        Assert.Contains($"[{family.Daughter.GrampsId}]", descendants);
    }

    [IntegrationFact]
    public async Task Relations_NameSiblingsAndTheirParents()
    {
        var family = await CreateFamilyAsync();

        var relation = await Session.CallAsync("get_relations", new
        {
            person1 = family.Son.GrampsId,
            person2 = family.Daughter.Handle
        });

        Assert.Contains($"({family.Daughter.GrampsId}) is the sister of", relation);
        Assert.Contains($"[handle: {family.Father.Handle}]", relation);
        Assert.Contains($"[handle: {family.Mother.Handle}]", relation);
    }

    [IntegrationFact]
    public async Task Timeline_IncludesRelativesEvents()
    {
        var family = await CreateFamilyAsync();

        var timeline = await Session.CallAsync("get_timeline", new { objectType = "person", identifier = family.Son.GrampsId });

        Assert.Contains("Timeline (3 events):", timeline);
        var lines = timeline.ReplaceLineEndings("\n").Split('\n');
        var marriage = Array.FindIndex(lines, line => line.Contains("about 1884: Marriage (Father)"));
        var sisterBirth = Array.FindIndex(lines, line => line.Contains("1885: Birth (Sister)"));
        var ownBirth = Array.FindIndex(lines, line => line.Contains("1890-05-03: Birth"));
        Assert.True(marriage >= 0 && marriage < sisterBirth && sisterBirth < ownBirth, timeline);
    }

    [IntegrationFact]
    public async Task ParallelCreates_GetDistinctGrampsIds()
    {
        var surname = TestNames.Surname();

        var people = await Task.WhenAll(Enumerable.Range(0, 6).Select(i =>
            Session.CreateAsync("people", new { primaryName = new { first = $"Child{(char)('a' + i)}", surname } })));

        Assert.Equal(6, people.Select(person => person.GrampsId).Distinct().Count());
        var listed = await Session.CallAsync("search", new { query = surname });
        Assert.Contains("Total: 6", listed);
    }

    private Task<CreatedRecord> CreatePersonAsync(string first, string surname, string gender) =>
        Session.CreateAsync("people", new { primaryName = new { first, surname }, gender });

    /// <summary>
    /// Ivan and Maria, married about 1884, with a son born 1890 and a daughter born 1885, linked in
    /// that order.
    /// </summary>
    private async Task<TestFamily> CreateFamilyAsync()
    {
        var surname = TestNames.Surname();
        var motherSurname = TestNames.Surname();
        var father = await CreatePersonAsync("Ivan", surname, "male");
        var mother = await CreatePersonAsync("Maria", motherSurname, "female");
        var son = await CreatePersonAsync("Petr", surname, "male");
        var daughter = await CreatePersonAsync("Anna", surname, "female");

        var sonBirth = await Session.CreateAsync("events", new { eventType = "Birth", date = "1890-05-03" });
        var daughterBirth = await Session.CreateAsync("events", new { eventType = "Birth", date = "1885" });
        var marriage = await Session.CreateAsync("events", new { eventType = "Marriage", date = "about 1884" });
        await Session.CallAsync("update_person", new { identifier = son.Handle, eventRefs = new[] { $"{sonBirth.Handle}::Primary" } });
        await Session.CallAsync("update_person", new { identifier = daughter.Handle, eventRefs = new[] { $"{daughterBirth.Handle}::Primary" } });

        var family = await Session.CreateAsync("families", new
        {
            fatherHandle = father.Handle,
            motherHandle = mother.Handle,
            relationshipType = "Married",
            childRefs = new object[] { $"{son.Handle}::Birth", new { @ref = daughter.Handle, frel = "Birth", mrel = "Birth" } },
            eventRefs = new[] { $"{marriage.Handle}::Family" }
        });
        return new TestFamily(surname, motherSurname, father, mother, son, daughter, family);
    }

    private sealed record TestFamily(
        string Surname,
        string MotherSurname,
        CreatedRecord Father,
        CreatedRecord Mother,
        CreatedRecord Son,
        CreatedRecord Daughter,
        CreatedRecord Family);
}
