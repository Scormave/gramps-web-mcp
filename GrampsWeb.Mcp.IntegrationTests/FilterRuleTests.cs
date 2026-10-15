using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace GrampsWeb.Mcp.IntegrationTests;

/// <summary>The rules argument of list_objects and the filter-rules reference, against Gramps Web's own filters.</summary>
[Collection(GrampsWebCollection.Name)]
public sealed class FilterRuleTests(GrampsWebFixture gramps)
{
    private McpSession Session => gramps.Session;

    /// <summary>
    /// Two families and a loner who share a first name, so gql on that name keeps other tests' people out:
    /// a father with his son, a second father with his son, and someone with no family.
    /// </summary>
    private sealed record Kin(string First, CreatedRecord Father, CreatedRecord Son, CreatedRecord Loner, CreatedRecord OtherFather, CreatedRecord OtherSon)
    {
        public string Gql => $"primary_name.first_name = {First}";
    }

    private async Task<Kin> CreateKinAsync()
    {
        var first = TestNames.Surname();
        Task<CreatedRecord> Person(string gender) =>
            Session.CreateAsync("create_person", new { primaryName = new { first, surname = TestNames.Surname() }, gender });

        var father = await Person("male");
        var son = await Person("male");
        var loner = await Person("female");
        var otherFather = await Person("male");
        var otherSon = await Person("male");
        await Session.CreateAsync("create_family", new { fatherHandle = father.Handle, childRefs = new[] { son.Handle } });
        await Session.CreateAsync("create_family", new { fatherHandle = otherFather.Handle, childRefs = new[] { otherSon.Handle } });
        return new Kin(first, father, son, loner, otherFather, otherSon);
    }

    [IntegrationFact]
    public async Task Rules_FindPeopleWithNoFamilyLinks()
    {
        var kin = await CreateKinAsync();

        var listed = await Session.CallAsync("list_objects", new
        {
            objectType = "people",
            gql = kin.Gql,
            rules = new { rules = new[] { new { name = "Disconnected" } } }
        });

        Assert.Contains("Total: 1", listed);
        Assert.Contains(kin.Loner.Handle, listed);
    }

    [IntegrationFact]
    public async Task Rules_InvertedKeepPeopleNotRelatedToSomeone()
    {
        var kin = await CreateKinAsync();

        var listed = await Session.CallAsync("list_objects", new
        {
            objectType = "people",
            gql = kin.Gql,
            rules = new { invert = true, rules = new[] { new { name = "IsRelatedWith", values = new[] { kin.Father.GrampsId } } } }
        });

        Assert.Contains("Total: 3", listed);
        Assert.Contains(kin.Loner.Handle, listed);
        Assert.Contains(kin.OtherFather.Handle, listed);
        Assert.Contains(kin.OtherSon.Handle, listed);
    }

    [IntegrationTheory]
    [InlineData(true, 2)]
    [InlineData(false, 1)]
    public async Task Rules_TakeTrueAndFalseValues(bool inclusive, int total)
    {
        var kin = await CreateKinAsync();

        // The array of rules alone is an "and" filter.
        var listed = await Session.CallAsync("list_objects", new
        {
            objectType = "people",
            gql = kin.Gql,
            rules = new[] { new { name = "IsAncestorOf", values = new object[] { kin.Son.GrampsId, inclusive } } }
        });

        Assert.Contains($"Total: {total}", listed);
        Assert.Contains(kin.Father.Handle, listed);
        Assert.Equal(inclusive, listed.Contains(kin.Son.Handle));
    }

    [IntegrationFact]
    public async Task Rules_MatchPeopleThroughTheirEvents()
    {
        var first = TestNames.Surname();
        var residence = await Session.CreateAsync("create_event", new { eventType = "Residence", date = "1897" });
        var birth = await Session.CreateAsync("create_event", new { eventType = "Birth", date = "1870" });
        var resident = await Session.CreateAsync("create_person", new
        {
            primaryName = new { first, surname = TestNames.Surname() },
            eventRefs = new[] { $"{residence.Handle}::Primary" }
        });
        await Session.CreateAsync("create_person", new
        {
            primaryName = new { first, surname = TestNames.Surname() },
            eventRefs = new[] { $"{birth.Handle}::Primary" }
        });

        var listed = await Session.CallAsync("list_objects", new
        {
            objectType = "people",
            gql = $"primary_name.first_name = {first}",
            rules = new { rules = new[] { new { @namespace = "Event", rules = new[] { new { name = "HasType", values = new[] { "Residence" } } } } } }
        });

        Assert.Contains("Total: 1", listed);
        Assert.Contains(resident.Handle, listed);
    }

    [IntegrationFact]
    public async Task Rules_UseASavedFilter()
    {
        var kin = await CreateKinAsync();
        var filterName = TestNames.Unique("Women");
        await SaveFilterAsync("people", new { name = filterName, comment = "Integration test", rules = new[] { new { name = "IsFemale" } } });

        // A JSON string works as well as JSON, for clients that send arguments as text.
        var listed = await Session.CallAsync("list_objects", new
        {
            objectType = "people",
            gql = kin.Gql,
            rules = JsonSerializer.Serialize(new { name = "MatchesFilter", values = new[] { filterName } })
        });
        var reference = await Session.CallAsync("get_reference", new { topic = "filter-rules", section = "people" });

        Assert.Contains("Total: 1", listed);
        Assert.Contains(kin.Loner.Handle, listed);
        Assert.Contains($"- {filterName}: IsFemale — Integration test", reference);
    }

    [IntegrationFact]
    public async Task Rules_NamingAMissingTagSayWhyNothingMatched()
    {
        var listed = await Session.CallAsync("list_objects", new
        {
            objectType = "people",
            rules = new[] { new { name = "HasTag", values = new[] { TestNames.Unique("tag") } } }
        });

        Assert.StartsWith("No people found. A Gramps ID, tag or saved filter name in the rules", listed);
    }

    [IntegrationFact]
    public async Task Rules_UnknownRuleIsNamedWithTheClosestOne()
    {
        var error = await Session.CallErrorAsync("list_objects", new
        {
            objectType = "people",
            rules = new[] { new { name = "IsRelatedwith", values = new[] { "I0001" } } }
        });

        // The image ships the FilterRules addon, whose IsRelatedWithFilterMatch is listed once Gramps has loaded its plugins.
        Assert.Contains("Unknown filter rule: IsRelatedwith for people (did you mean IsRelatedWith", error);
    }

    [IntegrationFact]
    public async Task Rules_RuleThatCannotRunKeepsTheServersReason()
    {
        // MatchesFilter needs the filter's name; without it Gramps fails while evaluating the rule.
        var error = await Session.CallErrorAsync("list_objects", new
        {
            objectType = "people",
            rules = new[] { new { name = "MatchesFilter" } }
        });

        Assert.Contains("Gramps Web could not apply the rules: Rule MatchesFilter could not be evaluated", error);
    }

    // Gramps Web API 3.22 names the labels only, 3.23 adds the kind of each value ("ID: person ID").
    [IntegrationTheory]
    [InlineData(null, "- people: ")]
    [InlineData("people", "- IsRelatedWith (ID")]
    [InlineData("people", "- Disconnected — ")]
    [InlineData("events", "- HasType (Event type")]
    public async Task Reference_FilterRulesListTheRulesOfGrampsWeb(string? section, string expected)
    {
        var text = await Session.CallAsync("get_reference", new { topic = "filter-rules", section });

        Assert.Contains(expected, text);
    }

    /// <summary>Saves a custom filter the way the Gramps Web front end does; the MCP server has no tool for it.</summary>
    private async Task SaveFilterAsync(string objectType, object filter)
    {
        using var http = new HttpClient(new TokenRoutePacer(new TokenRequestLog(), new HttpClientHandler()))
        {
            BaseAddress = new Uri(gramps.ApiUrl)
        };
        using var signIn = await http.PostAsJsonAsync(
            "/api/token/",
            new { username = GrampsWebFixture.OwnerName, password = gramps.OwnerPassword });
        signIn.EnsureSuccessStatusCode();
        var tokens = await signIn.Content.ReadFromJsonAsync<JsonElement>();

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/filters/{objectType}")
        {
            Content = JsonContent.Create(filter)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.GetProperty("access_token").GetString());
        using var response = await http.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }
}
