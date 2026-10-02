using System.Net;
using System.Text;
using System.Text.Json;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Config;
using GrampsWeb.Mcp.Formatters;
using GrampsWeb.Mcp.Models;
using GrampsWeb.Mcp.Serialization;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

public class PersonVitalEventRoleTests
{
    private const string ChildBirth = """{"handle":"child-birth","type":"Birth","date":{"modifier":0,"dateval":[1,2,2019,false]}}""";
    private const string OwnBirth = """{"handle":"own-birth","type":"Birth","date":{"modifier":0,"dateval":[15,3,1990,false]}}""";

    [Theory]
    [InlineData("\"Father\"")]
    [InlineData("""{"_class":"EventRoleType","string":"Mother"}""")]
    [InlineData("\"Witness\"")]
    public async Task ExtractEventInfo_IgnoresBirthWherePersonIsNotPrimary(string role)
    {
        using var handler = new EventHandler(new() { ["child-birth"] = ChildBirth });
        var person = Person($$"""
            {"handle":"parent","birth_ref_index":-1,
             "event_ref_list":[{"ref":"child-birth","role":{{role}}}]}
            """);

        var birth = await PersonFormatter.ExtractEventInfo(person, "Birth", Client(handler));

        Assert.Null(birth);
        Assert.DoesNotContain("/api/events/child-birth", handler.Paths);
    }

    [Theory]
    [InlineData(",\"role\":\"Primary\"")]
    [InlineData(",\"role\":{\"_class\":\"EventRoleType\",\"string\":\"Primary\"}")]
    [InlineData(",\"role\":{\"_class\":\"EventRoleType\",\"value\":1}")]
    [InlineData("")]
    public async Task ExtractEventInfo_FallsBackToPrimaryBirth_SkippingOtherRoles(string ownRole)
    {
        using var handler = new EventHandler(new() { ["child-birth"] = ChildBirth, ["own-birth"] = OwnBirth });
        var person = Person($$"""
            {"handle":"parent","birth_ref_index":-1,
             "event_ref_list":[{"ref":"child-birth","role":"Father"},{"ref":"own-birth"{{ownRole}}}]}
            """);

        var birth = await PersonFormatter.ExtractEventInfo(person, "Birth", Client(handler));

        Assert.NotNull(birth);
        Assert.Contains("1990", birth);
        Assert.DoesNotContain("/api/events/child-birth", handler.Paths);
    }

    [Fact]
    public async Task FormatPersonExtended_IgnoresPreloadedBirthWherePersonIsNotPrimary()
    {
        using var handler = new EventHandler([]);
        var person = JsonSerializer.Deserialize<GrampsPersonExtended>($$$"""
            {"handle":"parent","gramps_id":"I0001","birth_ref_index":-1,
             "event_ref_list":[{"ref":"child-birth","role":"Father"}],
             "extended":{"events":[{{{ChildBirth}}}]}}
            """, GrampsJson.Options)!;

        var result = await PersonFormatter.FormatPersonExtended(person, Client(handler));

        Assert.DoesNotContain(result.Split('\n'), l => l.StartsWith("Birth:"));
        Assert.Contains("Birth: 2019-02-01 [Father]", result);
    }

    [Fact]
    public async Task FormatPersonExtended_UsesPreloadedPrimaryBirth()
    {
        using var handler = new EventHandler([]);
        var person = JsonSerializer.Deserialize<GrampsPersonExtended>($$$"""
            {"handle":"parent","gramps_id":"I0001","birth_ref_index":-1,
             "event_ref_list":[{"ref":"child-birth","role":"Father"},{"ref":"own-birth","role":"Primary"}],
             "extended":{"events":[{{{ChildBirth}}},{{{OwnBirth}}}]}}
            """, GrampsJson.Options)!;

        var result = await PersonFormatter.FormatPersonExtended(person, Client(handler));

        var birthLine = Assert.Single(result.Split('\n'), l => l.StartsWith("Birth:"));
        Assert.Contains("1990", birthLine);
        Assert.Contains("[event: own-birth]", birthLine);
    }

    private static GrampsPerson Person(string json) =>
        JsonSerializer.Deserialize<GrampsPerson>(json, GrampsJson.Options)!;

    private static GrampsApiClient Client(HttpMessageHandler handler)
    {
        var config = new GrampsConfig("https://gramps.test", "user", "pass", "tree");
        var http = new HttpClient(handler, disposeHandler: false);
        return new(http, config, NullLogger<GrampsApiClient>.Instance,
            new GrampsAuthTokenProvider(http, config, NullLogger<GrampsAuthTokenProvider>.Instance));
    }

    private sealed class EventHandler(Dictionary<string, string> events) : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.PathAndQuery;
            if (path.StartsWith("/api/token/", StringComparison.Ordinal))
                return Task.FromResult(Json("""{"access_token":"token","refresh_token":"refresh","expires_in":900}"""));

            Paths.Add(path);
            if (path.StartsWith("/api/types/", StringComparison.Ordinal))
                return Task.FromResult(Json("[]"));

            const string eventsPrefix = "/api/events/";
            if (path.StartsWith(eventsPrefix, StringComparison.Ordinal) &&
                events.TryGetValue(path[eventsPrefix.Length..].Split('?')[0], out var body))
                return Task.FromResult(Json(body));

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("not found", Encoding.UTF8, "text/plain")
            });
        }

        private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }
}
