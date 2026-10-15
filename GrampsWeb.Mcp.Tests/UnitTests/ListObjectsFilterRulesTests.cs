using System.Net;
using System.Text;
using System.Text.Json;
using System.Web;
using GrampsWeb.Mcp.Input;
using GrampsWeb.Mcp.Tools;
using ModelContextProtocol;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

public class ListObjectsFilterRulesTests
{
    private static FlexibleFilterRules Rules(string json) =>
        JsonSerializer.Deserialize<FlexibleFilterRules>(json)!;

    [Fact]
    public async Task ListObjects_SendsTheRulesWithGql()
    {
        using var handler = new ListHandler();

        await SearchTools.ListObjects(
            "people",
            gql: "gender = 1",
            rules: Rules("""{"invert": true, "rules": [{"name": "IsRelatedWith", "values": ["I0001"]}]}"""),
            client: FilterRulesTests.CreateClient(handler));

        var query = HttpUtility.ParseQueryString(new Uri("https://gramps-web.test" + handler.ListPath).Query);
        Assert.StartsWith("/api/people/?", handler.ListPath);
        Assert.Equal("""{"invert":true,"rules":[{"name":"IsRelatedWith","values":["I0001"]}]}""", query["rules"]);
        Assert.Equal("gender = 1", query["gql"]);
    }

    [Fact]
    public async Task ListObjects_RejectsGrampsIdWithRules()
    {
        using var handler = new ListHandler();

        var error = await Assert.ThrowsAsync<McpException>(() => SearchTools.ListObjects(
            "people",
            grampsId: "I0001",
            rules: Rules("""[{"name": "IsMale"}]"""),
            client: FilterRulesTests.CreateClient(handler)));

        Assert.StartsWith("Pass grampsId or rules, not both;", error.Message);
        Assert.Null(handler.ListPath);
    }

    [Fact]
    public async Task ListObjects_RejectsInvalidRulesBeforeAnyRequest()
    {
        using var handler = new ListHandler();

        var error = await Assert.ThrowsAsync<McpException>(() => SearchTools.ListObjects(
            "events",
            rules: Rules("""[{"name": "HasType", "values": "Birth", "regexp": true}]"""),
            client: FilterRulesTests.CreateClient(handler)));

        Assert.StartsWith("Invalid rules at rules[0]: has an unknown key \"regexp\"", error.Message);
        Assert.Null(handler.ListPath);
    }

    [Fact]
    public async Task ListObjects_SaysWhyRulesMayMatchNothing()
    {
        using var handler = new ListHandler();

        var result = await SearchTools.ListObjects(
            "people",
            rules: Rules("""[{"name": "HasTag", "values": ["Nonexistent"]}]"""),
            client: FilterRulesTests.CreateClient(handler));

        Assert.Equal(
            "No people found. A Gramps ID, tag or saved filter name in the rules that the tree does not have matches nothing; check those values.",
            result);
    }

    [Fact]
    public async Task ListObjects_WithoutRulesKeepsThePlainEmptyAnswer()
    {
        using var handler = new ListHandler();

        var result = await SearchTools.ListObjects("people", client: FilterRulesTests.CreateClient(handler));

        Assert.Equal("No people found.", result);
    }

    [Fact]
    public async Task ListObjects_ExplainsAnUnknownRule()
    {
        using var handler = new ListHandler { ListStatus = HttpStatusCode.NotFound };

        var error = await Assert.ThrowsAsync<McpException>(() => SearchTools.ListObjects(
            "people",
            rules: Rules("""[{"name": "IsDisconnected"}]"""),
            client: FilterRulesTests.CreateClient(handler)));

        Assert.StartsWith("Unknown filter rule: IsDisconnected for people (did you mean Disconnected?).", error.Message);
    }

    [Fact]
    public async Task ListObjects_KeepsTheServersMessageWhenARuleFails()
    {
        using var handler = new ListHandler
        {
            ListStatus = HttpStatusCode.UnprocessableEntity,
            ListBody = """{"error": {"code": 422, "message": "Rule MatchesFilter could not be evaluated: list index out of range"}}"""
        };

        var error = await Assert.ThrowsAsync<McpException>(() => SearchTools.ListObjects(
            "people",
            rules: Rules("""[{"name": "MatchesFilter"}]"""),
            client: FilterRulesTests.CreateClient(handler)));

        Assert.Equal(
            "Gramps Web could not apply the rules: Rule MatchesFilter could not be evaluated: list index out of range",
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
