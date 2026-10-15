using System.Net;
using System.Text;
using System.Text.Json;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Config;
using GrampsWeb.Mcp.Exceptions;
using GrampsWeb.Mcp.Input;
using GrampsWeb.Mcp.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

public class FilterRulesTests
{
    private static FlexibleFilterRules Rules(string json) =>
        JsonSerializer.Deserialize<FlexibleFilterRules>(json)!;

    private static string Build(string json, string objectType = "people") =>
        FilterRules.Build(Rules(json), objectType).Json;

    private static string BuildError(string json, string objectType = "people") =>
        Assert.Throws<McpException>(() => FilterRules.Build(Rules(json), objectType)).Message;

    [Fact]
    public void Build_KeepsAFilterAsGrampsWebTakesIt()
    {
        var json = Build("""
            {"function": "OR", "invert": true,
             "rules": [{"name": "IsAncestorOf", "values": ["I0001", true]}, {"name": "IsMale", "regex": false}]}
            """);

        Assert.Equal(
            """{"function":"or","invert":true,"rules":[{"name":"IsAncestorOf","values":["I0001",true]},{"name":"IsMale","regex":false}]}""",
            json);
    }

    [Fact]
    public void Build_TakesAnArrayAsTheRulesOfAnAndFilter()
    {
        Assert.Equal(
            """{"rules":[{"name":"Disconnected"},{"name":"SearchName","values":["Иванов"]}]}""",
            Build("""[{"name": "Disconnected"}, {"name": "SearchName", "values": ["Иванов"]}]"""));
    }

    [Fact]
    public void Build_TakesOneRuleAsAFilterOfIt()
    {
        Assert.Equal(
            """{"rules":[{"name":"HasTag","values":["ToDo"]}]}""",
            Build("""{"name": "HasTag", "values": "ToDo"}"""));
    }

    [Fact]
    public void Build_ReadsRulesGivenAsAJsonString()
    {
        Assert.Equal(
            """{"invert":true,"rules":[{"name":"IsRelatedWith","values":["I0001"]}]}""",
            Build("""
                "{\"invert\": true, \"rules\": [{\"name\": \"IsRelatedWith\", \"values\": [\"I0001\"]}]}"
                """));
    }

    [Fact]
    public void Build_WritesTheNamespaceOfNestedFiltersOnly()
    {
        var json = Build("""
            {"namespace": "people", "rules": [
              {"namespace": "events", "rules": [
                {"name": "HasType", "values": ["Residence"]},
                {"namespace": "Place", "rules": [{"name": "HasTitle", "values": ["Ярославль"]}]}]}]}
            """);

        Assert.Equal(
            """{"rules":[{"namespace":"Event","rules":[{"name":"HasType","values":["Residence"]},""" +
            """{"namespace":"Place","rules":[{"name":"HasTitle","values":["Ярославль"]}]}]}]}""",
            json);
    }

    [Fact]
    public void Build_CollectsEveryRuleNameWithItsNamespace()
    {
        var request = FilterRules.Build(
            Rules("""[{"name": "IsMale"}, {"namespace": "Event", "rules": [{"name": "HasType", "values": ["Birth"]}]}]"""),
            "people");

        Assert.Equal(
            [new FilterRules.RuleName("Person", "IsMale"), new FilterRules.RuleName("Event", "HasType")],
            request.RuleNames);
    }

    [Fact]
    public void Build_RejectsTagsWhichHaveNoRules()
    {
        Assert.Equal(
            "rules are not available for tags; they work with people, families, events, places, citations, sources, repositories, media, notes.",
            BuildError("""[{"name": "Everyone"}]""", "tags"));
    }

    [Theory]
    [InlineData("\"{not json\"", "rules is not valid JSON: ")]
    [InlineData("42", "rules must be a JSON object such as {\"rules\": [{\"name\": \"IsMale\"}]} or an array of rules, not the number 42.")]
    [InlineData("""{"invert": true}""", "Invalid rules: the filter needs rules: a non-empty array of rules and nested filters.")]
    [InlineData("""{"rules": []}""", "Invalid rules at rules: must be a non-empty array of rules and nested filters.")]
    [InlineData("""{"rules": [{"name": "IsMale"}], "negate": true}""", "Invalid rules: the filter has an unknown key \"negate\"; a filter takes function, invert, namespace and rules.")]
    [InlineData("""{"function": "xor", "rules": [{"name": "IsMale"}]}""", "Invalid rules at function: must be and, or or one, not \"xor\".")]
    [InlineData("""{"invert": "yes", "rules": [{"name": "IsMale"}]}""", "Invalid rules at invert: must be true or false, not \"yes\".")]
    [InlineData("""["IsMale"]""", "Invalid rules at rules[0]: must be a rule {\"name\": …} or a nested filter {\"rules\": […]}, not \"IsMale\".")]
    [InlineData("""[{"values": ["I0001"]}]""", "Invalid rules at rules[0]: needs name (a rule) or rules (a nested filter).")]
    [InlineData("""[{"name": " "}]""", "Invalid rules at rules[0].name: must be a rule name such as IsMale, not \" \".")]
    [InlineData("""{"name": "IsMale", "invert": true}""", "Invalid rules at rules[0]: has an unknown key \"invert\"; a rule takes name, values and regex.")]
    [InlineData("""[{"name": "HasIdOf", "values": [["I0001"]]}]""", "Invalid rules at rules[0].values[0]: must be text, a number or true/false, not an array.")]
    [InlineData("""[{"name": "SearchName", "values": ["a"], "regex": 1}]""", "Invalid rules at rules[0].regex: must be true or false, not the number 1.")]
    public void Build_NamesTheMistakeByItsPlace(string json, string expected)
    {
        Assert.StartsWith(expected, BuildError(json));
    }

    [Theory]
    [InlineData("""{"namespace": "Family", "rules": [{"name": "IsMale"}]}""", "Invalid rules at namespace: must be the listed type (Person) on the top filter")]
    [InlineData("""[{"namespace": "Planet", "rules": [{"name": "IsMale"}]}]""", "Invalid rules at rules[0].namespace: must be one of Person, Family, Event, Place, Citation, Source, Repository, Media, Note, not \"Planet\".")]
    [InlineData("""[{"namespace": "Place", "rules": [{"name": "HasTitle"}]}]""", "Invalid rules at rules[0].namespace: Place cannot be matched from Person; a nested filter in Person can match Event or Family.")]
    public void Build_ChecksTheNamespaceOfNestedFilters(string json, string expected)
    {
        Assert.StartsWith(expected, BuildError(json));
    }

    [Fact]
    public void Build_ExplainsANamespaceWithoutBridges()
    {
        Assert.Equal(
            "Invalid rules at rules[0].namespace: Person cannot be matched from Note; a nested filter in Note cannot change namespace.",
            BuildError("""[{"namespace": "Person", "rules": [{"name": "IsMale"}]}]""", "notes"));
    }

    [Fact]
    public void Build_TakesFourLevelsOfNestedFilters()
    {
        Assert.Contains("IsMale", Build(Nested(4)));

        Assert.StartsWith(
            "Invalid rules at rules[0].rules[0].rules[0].rules[0].rules[0]: is nested too deep",
            BuildError(Nested(5)));
    }

    private static string Nested(int levels)
    {
        var json = """{"rules": [{"name": "IsMale"}]}""";
        for (var level = 0; level < levels; level++)
            json = $$"""{"rules": [{{json}}]}""";
        return json;
    }

    [Fact]
    public void Converter_KeepsTheJsonAndReadsNullAsNoRules()
    {
        Assert.Null(JsonSerializer.Deserialize<FlexibleFilterRules?>("null"));

        var rules = Rules("""[{"name": "IsMale"}]""");
        Assert.Equal("""[{"name":"IsMale"}]""", JsonSerializer.Serialize(rules));
    }

    [Fact]
    public async Task ExplainFailure_NamesUnknownRulesWithTheClosestKnownOnes()
    {
        using var handler = new CatalogHandler();
        var request = FilterRules.Build(
            Rules("""[{"name": "IsRelatedwith", "values": ["I0001"]}, {"name": "Disconected"}, {"name": "IsMale"}, {"name": "Frobnicate"}]"""),
            "people");

        var message = await FilterRules.ExplainFailureAsync(
            new GrampsApiException(HttpStatusCode.NotFound, ""), request, CreateClient(handler));

        Assert.Equal(
            "Unknown filter rules: IsRelatedwith for people (did you mean IsRelatedWith?); " +
            "Disconected for people (did you mean Disconnected?); Frobnicate for people. " +
            "get_reference with topic filter-rules and the type as section (such as people) lists the rules and their values.",
            message);
        Assert.Equal(["/api/filters/people"], handler.Paths);
    }

    [Fact]
    public async Task ExplainFailure_ChecksNestedRulesAgainstTheirOwnType()
    {
        using var handler = new CatalogHandler();
        var request = FilterRules.Build(
            Rules("""[{"name": "IsMale"}, {"namespace": "Event", "rules": [{"name": "HasTyp", "values": ["Birth"]}]}]"""),
            "people");

        var message = await FilterRules.ExplainFailureAsync(
            new GrampsApiException(HttpStatusCode.NotFound, ""), request, CreateClient(handler));

        Assert.StartsWith("Unknown filter rule: HasTyp for events (did you mean HasType?). ", message);
        Assert.Equal(["/api/filters/people", "/api/filters/events"], handler.Paths);
    }

    [Fact]
    public async Task ExplainFailure_PointsMatchesQueryToGqlOnOlderServers()
    {
        using var handler = new CatalogHandler();
        var request = FilterRules.Build(Rules("""[{"name": "MatchesQuery", "values": ["gender = 1"]}]"""), "people");

        var message = await FilterRules.ExplainFailureAsync(
            new GrampsApiException(HttpStatusCode.NotFound, ""), request, CreateClient(handler));

        Assert.StartsWith(
            "Unknown filter rule: MatchesQuery for people needs Gramps Web API 3.23 or later; use the gql argument instead. ",
            message);
    }

    [Fact]
    public async Task ExplainFailure_KeepsA404WhenEveryRuleIsKnown()
    {
        using var handler = new CatalogHandler();
        var request = FilterRules.Build(Rules("""[{"name": "IsMale"}]"""), "people");

        Assert.Null(await FilterRules.ExplainFailureAsync(
            new GrampsApiException(HttpStatusCode.NotFound, ""), request, CreateClient(handler)));
    }

    [Fact]
    public async Task ExplainFailure_KeepsA404WhenTheRuleListCannotBeRead()
    {
        using var handler = new CatalogHandler { CatalogStatus = HttpStatusCode.Forbidden };
        var request = FilterRules.Build(Rules("""[{"name": "IsMael"}]"""), "people");

        Assert.Null(await FilterRules.ExplainFailureAsync(
            new GrampsApiException(HttpStatusCode.NotFound, ""), request, CreateClient(handler)));
    }

    [Theory]
    [InlineData(HttpStatusCode.UnprocessableEntity, """{"error": {"code": 422, "message": "Rule IsAncestorOf could not be evaluated: list index out of range"}}""",
        "Gramps Web could not apply the rules: Rule IsAncestorOf could not be evaluated: list index out of range")]
    [InlineData(HttpStatusCode.BadRequest, """{"error": {"code": 400, "message": "Unsupported namespace bridge: Person → Place"}}""",
        "Gramps Web could not apply the rules: Unsupported namespace bridge: Person → Place")]
    [InlineData(HttpStatusCode.UnprocessableEntity, """{"error": {"code": 422, "message": "Filter does not adhere to schema"}}""",
        "Gramps Web could not apply the rules: Filter does not adhere to schema")]
    [InlineData(HttpStatusCode.BadRequest, """{"error": {"code": 400, "message": "Invalid GQL"}}""", null)]
    [InlineData(HttpStatusCode.UnprocessableEntity, "not json", null)]
    [InlineData(HttpStatusCode.InternalServerError, """{"error": {"code": 500, "message": "Rule X"}}""", null)]
    public async Task ExplainFailure_KeepsTheServersMessageAboutRules(HttpStatusCode status, string body, string? expected)
    {
        using var handler = new CatalogHandler();
        var request = FilterRules.Build(Rules("""[{"name": "IsMale"}]"""), "people");

        Assert.Equal(expected, await FilterRules.ExplainFailureAsync(
            new GrampsApiException(status, body), request, CreateClient(handler)));
        Assert.Empty(handler.Paths);
    }

    internal static GrampsApiClient CreateClient(HttpMessageHandler handler)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://gramps-web.test") };
        var config = new GrampsConfig("https://gramps-web.test", "user", "pass", "tree");
        var provider = new GrampsAuthTokenProvider(http, config, NullLogger<GrampsAuthTokenProvider>.Instance);
        return new GrampsApiClient(http, config, NullLogger<GrampsApiClient>.Instance, provider);
    }

    /// <summary>Answers <c>/api/filters/{type}</c> with a few rules of people and events.</summary>
    internal sealed class CatalogHandler : HttpMessageHandler
    {
        public HttpStatusCode CatalogStatus { get; init; } = HttpStatusCode.OK;

        public List<string> Paths { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.PathAndQuery;
            if (path.StartsWith("/api/token/", StringComparison.Ordinal))
                return Task.FromResult(Json(HttpStatusCode.OK, """{"access_token":"token","refresh_token":"refresh","expires_in":900}"""));

            Paths.Add(path);
            var rules = path switch
            {
                "/api/filters/people" => new[] { "IsRelatedWith", "Disconnected", "IsMale", "IsFemale", "HasIdOf" },
                "/api/filters/events" => ["HasType", "HasIdOf"],
                _ => throw new Xunit.Sdk.XunitException($"Unexpected request: {path}")
            };
            var body = JsonSerializer.Serialize(new
            {
                filters = Array.Empty<object>(),
                rules = rules.Select(rule => new { rule, name = rule, category = "General filters", description = "", labels = Array.Empty<string>() })
            });
            return Task.FromResult(Json(CatalogStatus, CatalogStatus == HttpStatusCode.OK ? body : "{}"));
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string json) => new(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }
}
