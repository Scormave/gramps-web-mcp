using System.Net;
using System.Text;
using System.Text.Json;
using System.Web;
using GrampsWeb.Mcp.Input;
using GrampsWeb.Mcp.Tools;
using ModelContextProtocol;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

public class ListRecordsFilterRulesTests
{
    private static FlexibleFilterRules Rules(string json) =>
        JsonSerializer.Deserialize<FlexibleFilterRules>(json)!;

    [Fact]
    public async Task ListRecords_SendsTheQueryAsAMatchesQueryRule()
    {
        using var handler = new ListHandler();

        await SearchTools.ListRecords(
            "people",
            query: " primary_name.first_name == 'Anna' and len(media_list) >= 1 ",
            client: FilterRulesTests.CreateClient(handler));

        var query = HttpUtility.ParseQueryString(new Uri("https://gramps-web.test" + handler.ListPath).Query);
        Assert.StartsWith("/api/people/?", handler.ListPath);
        Assert.Equal(
            """{"rules":[{"name":"MatchesQuery","values":["primary_name.first_name == 'Anna' and len(media_list) >= 1"]}]}""",
            query["rules"]);
        Assert.Null(query["gql"]);
    }

    [Theory]
    [InlineData("""[{"name": "IsMale"}]""",
        """{"rules":[{"name":"IsMale"},{"name":"MatchesQuery","values":["len(media_list) >= 1"]}]}""")]
    [InlineData("""{"invert": true, "rules": [{"name": "IsRelatedWith", "values": ["I0001"]}]}""",
        """{"rules":[{"invert":true,"rules":[{"name":"IsRelatedWith","values":["I0001"]}]},{"name":"MatchesQuery","values":["len(media_list) >= 1"]}]}""")]
    public async Task ListRecords_SendsTheQueryBesideTheRules(string rules, string expected)
    {
        using var handler = new ListHandler();

        await SearchTools.ListRecords(
            "people",
            query: "len(media_list) >= 1",
            rules: Rules(rules),
            client: FilterRulesTests.CreateClient(handler));

        var query = HttpUtility.ParseQueryString(new Uri("https://gramps-web.test" + handler.ListPath).Query);
        Assert.Equal(expected, query["rules"]);
    }

    [Theory]
    [InlineData(null, "len(media_list) >= 1")]
    [InlineData("""[{"name": "IsMale"}]""", null)]
    public async Task ListRecords_RejectsGrampsIdWithAFilter(string? rules, string? query)
    {
        using var handler = new ListHandler();

        var error = await Assert.ThrowsAsync<McpException>(() => SearchTools.ListRecords(
            "people",
            grampsId: "I0001",
            query: query,
            rules: rules == null ? null : Rules(rules),
            client: FilterRulesTests.CreateClient(handler)));

        Assert.StartsWith("Pass grampsId or a filter (query, rules), not both;", error.Message);
        Assert.Null(handler.ListPath);
    }

    [Fact]
    public async Task ListRecords_RejectsAQueryForTags()
    {
        using var handler = new ListHandler();

        var error = await Assert.ThrowsAsync<McpException>(() => SearchTools.ListRecords(
            "tags",
            query: "name == 'ToDo'",
            client: FilterRulesTests.CreateClient(handler)));

        Assert.StartsWith("query is not available for tags; it works with people,", error.Message);
        Assert.Null(handler.ListPath);
    }

    [Fact]
    public async Task ListRecords_RejectsInvalidRulesBeforeAnyRequest()
    {
        using var handler = new ListHandler();

        var error = await Assert.ThrowsAsync<McpException>(() => SearchTools.ListRecords(
            "events",
            rules: Rules("""[{"name": "HasType", "values": "Birth", "regexp": true}]"""),
            client: FilterRulesTests.CreateClient(handler)));

        Assert.StartsWith("Invalid rules at rules[0]: has an unknown key \"regexp\"", error.Message);
        Assert.Null(handler.ListPath);
    }

    [Fact]
    public async Task ListRecords_SaysWhyRulesMayMatchNothing()
    {
        using var handler = new ListHandler();

        var result = await SearchTools.ListRecords(
            "people",
            rules: Rules("""[{"name": "HasTag", "values": ["Nonexistent"]}]"""),
            client: FilterRulesTests.CreateClient(handler));

        Assert.Equal(
            "No people found. A Gramps ID, tag or saved filter name in the rules that the tree does not have matches nothing; check those values.",
            result);
    }

    [Fact]
    public async Task ListRecords_WithoutRulesKeepsThePlainEmptyAnswer()
    {
        using var handler = new ListHandler();

        var result = await SearchTools.ListRecords("people", client: FilterRulesTests.CreateClient(handler));

        Assert.Equal("No people found.", result);
    }

    [Fact]
    public async Task ListRecords_ExplainsAnUnknownRule()
    {
        using var handler = new ListHandler { ListStatus = HttpStatusCode.NotFound };

        var error = await Assert.ThrowsAsync<McpException>(() => SearchTools.ListRecords(
            "people",
            rules: Rules("""[{"name": "IsDisconnected"}]"""),
            client: FilterRulesTests.CreateClient(handler)));

        Assert.StartsWith("Unknown filter rule: IsDisconnected for people (did you mean Disconnected?).", error.Message);
    }

    [Fact]
    public async Task ListRecords_KeepsTheServersMessageWhenARuleFails()
    {
        using var handler = new ListHandler
        {
            ListStatus = HttpStatusCode.UnprocessableEntity,
            ListBody = """{"error": {"code": 422, "message": "Rule MatchesFilter could not be evaluated: list index out of range"}}"""
        };

        var error = await Assert.ThrowsAsync<McpException>(() => SearchTools.ListRecords(
            "people",
            rules: Rules("""[{"name": "MatchesFilter"}]"""),
            client: FilterRulesTests.CreateClient(handler)));

        Assert.Equal(
            "Gramps Web could not apply the rules: Rule MatchesFilter could not be evaluated: list index out of range",
            error.Message);
    }

    [Fact]
    public async Task ListRecords_WithOnlyAQueryKeepsThePlainEmptyAnswer()
    {
        using var handler = new ListHandler();

        var result = await SearchTools.ListRecords(
            "people", query: "gramps_id == 'I9999'", client: FilterRulesTests.CreateClient(handler));

        Assert.Equal("No people found.", result);
    }

    // Gramps Web API 3.26 answers an invalid GOQL expression this way, the reason at the place of the rule.
    [Fact]
    public async Task ListRecords_ExplainsAQuerySyntaxError()
    {
        using var handler = new ListHandler
        {
            ListStatus = HttpStatusCode.UnprocessableEntity,
            ListBody = """{"error": {"code": 422, "message": "Invalid MatchesQuery expression", "type": "invalid_query", "messages": {"query": {"rules": {"rules": {"0": {"values": {"0": ["invalid syntax: invalid syntax (<unknown>, line 1)"]}}}}}}}}"""
        };

        var error = await Assert.ThrowsAsync<McpException>(() => SearchTools.ListRecords(
            "people",
            query: "gender = 1",
            client: FilterRulesTests.CreateClient(handler)));

        Assert.Equal(
            "Gramps Web could not run the GOQL query: invalid syntax: invalid syntax (<unknown>, line 1). " +
            "GOQL is Python-like: == for equals, text in quotes, and, or, not; for example primary_name.first_name == 'Anna'.",
            error.Message);
    }

    // Gramps Web API 3.23 to 3.25 put the reason in the message of the rule that failed.
    [Theory]
    [InlineData(
        "Rule MatchesQuery could not be evaluated: invalid syntax: invalid syntax (<unknown>, line 1)",
        "Gramps Web could not run the GOQL query: invalid syntax: invalid syntax (<unknown>, line 1). " +
        "GOQL is Python-like: == for equals, text in quotes, and, or, not; for example primary_name.first_name == 'Anna'.")]
    [InlineData(
        "Rule MatchesQuery could not be evaluated: unknown field 'first_name' on Person -- known fields: gender, primary_name",
        "Gramps Web could not run the GOQL query: unknown field 'first_name' on Person -- known fields: gender, primary_name.")]
    public async Task ListRecords_ExplainsAQueryError_OfOlderServers(string message, string expected)
    {
        using var handler = new ListHandler
        {
            ListStatus = HttpStatusCode.UnprocessableEntity,
            ListBody = JsonSerializer.Serialize(new { error = new { code = 422, message } })
        };

        var error = await Assert.ThrowsAsync<McpException>(() => SearchTools.ListRecords(
            "people",
            query: "gender = 1",
            client: FilterRulesTests.CreateClient(handler)));

        Assert.Equal(expected, error.Message);
    }

    [Fact]
    public async Task ListRecords_ExplainsAnUnknownQueryField_InANestedFilter()
    {
        using var handler = new ListHandler
        {
            ListStatus = HttpStatusCode.UnprocessableEntity,
            ListBody = """{"error": {"code": 422, "message": "Invalid MatchesQuery expression", "type": "invalid_query", "messages": {"query": {"rules": {"rules": {"1": {"values": {"0": ["unknown field 'first_name' on Person -- known fields: gender, gramps_id, primary_name"]}}}}}}}}"""
        };

        var error = await Assert.ThrowsAsync<McpException>(() => SearchTools.ListRecords(
            "people",
            query: "first_name == 'Anna'",
            rules: Rules("""{"function": "or", "rules": [{"name": "IsMale"}]}"""),
            client: FilterRulesTests.CreateClient(handler)));

        Assert.Equal(
            "Gramps Web could not run the GOQL query: unknown field 'first_name' on Person -- known fields: gender, gramps_id, primary_name.",
            error.Message);
    }

    /// <summary>Answers the people list, and the rule list of people for a 404 explanation.</summary>
    private sealed class ListHandler : HttpMessageHandler
    {
        public HttpStatusCode ListStatus { get; init; } = HttpStatusCode.OK;

        public string ListBody { get; init; } = "[]";

        public string? ListPath { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.PathAndQuery;
            if (path.StartsWith("/api/token/", StringComparison.Ordinal))
                return Task.FromResult(Json(HttpStatusCode.OK, """{"access_token":"token","refresh_token":"refresh","expires_in":900}"""));

            if (path == "/api/filters/people")
                return Task.FromResult(Json(HttpStatusCode.OK,
                    """{"filters": [], "rules": [{"rule": "Disconnected"}, {"rule": "IsRelatedWith"}]}"""));

            Assert.Matches("^/api/(people|events)/\\?", path);
            ListPath = path;
            var response = Json(ListStatus, ListBody);
            if (ListStatus == HttpStatusCode.OK)
                response.Headers.Add("X-Total-Count", "0");
            return Task.FromResult(response);
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string json) => new(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }
}
