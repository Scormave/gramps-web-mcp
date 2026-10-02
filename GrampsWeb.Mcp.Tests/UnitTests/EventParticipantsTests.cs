using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Config;
using GrampsWeb.Mcp.Formatters;
using GrampsWeb.Mcp.Models;
using GrampsWeb.Mcp.Serialization;
using GrampsWeb.Mcp.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

public class EventParticipantsTests
{
    private const string KidBirthParticipants = """
        { "participants": { "people": [
          { "role": "Primary", "person": { "handle": "kid", "name_display": "Ivanova, Evdokia", "gramps_id": "I0203" } },
          { "role": "Father", "person": { "handle": "anchor", "name_display": "Ivanov, Egor", "gramps_id": "I0113" } },
          { "role": "Mother", "person": { "handle": "wife", "name_display": "Ivanova, Anastasia", "gramps_id": "I0202" } }
        ] } }
        """;

    [Fact]
    public async Task PersonTimeline_Names_The_Others_In_Events_Under_Another_Role()
    {
        var handler = new PathHandler(new Dictionary<string, string>
        {
            ["/api/people/anchor/timeline?discard_empty=false"] = """
                [
                  { "handle": "own-birth", "type": "Birth", "date": "1820", "role": "Primary", "person": { "relationship": "self" } },
                  { "handle": "kid-birth", "type": "Birth", "date": "1856-08-01", "role": "Father", "age": "36 years",
                    "person": { "relationship": "self" } },
                  { "handle": "wedding", "type": "Marriage", "date": "1858-10-27", "role": "Witness",
                    "description": "Church record", "person": { "relationship": "self" } },
                  { "handle": "census", "type": "Census", "date": "1860", "role": "Informant", "person": { "relationship": "self" } },
                  { "handle": "son-wedding", "label": "Marriage (Son)", "type": "Marriage", "date": "1874", "role": "Family",
                    "person": { "handle": "son", "gramps_id": "I0167", "name_display": "Ivanov, Larion", "relationship": "son" } }
                ]
                """,
            // Only the timeline person's own events under another role; nobody else is in the census.
            ["/api/events/?handles=kid-birth,wedding,census&profile=participants&page=1&pagesize=3"] = $$"""
                [
                  { "handle": "kid-birth", "type": "Birth", "profile": {{KidBirthParticipants}} },
                  { "handle": "wedding", "type": "Marriage", "profile": { "participants": {
                    "people": [
                      { "role": "Groom", "person": { "handle": "groom", "name_display": "Petrov, Ivan", "gramps_id": "I0301" } },
                      { "role": "Bride", "person": { "handle": "bride", "name_display": "Petrova, Maria", "gramps_id": "I0302" } },
                      { "role": "Witness", "person": { "handle": "anchor", "name_display": "Ivanov, Egor", "gramps_id": "I0113" } }
                    ],
                    "families": [
                      { "role": "Family", "family": { "gramps_id": "F0060",
                        "father": { "name_display": "Petrov, Ivan" }, "mother": { "name_display": "Petrova, Maria" } } }
                    ] } } },
                  { "handle": "census", "type": "Census", "profile": { "participants": { "people": [
                    { "role": "Informant", "person": { "handle": "anchor", "name_display": "Ivanov, Egor", "gramps_id": "I0113" } }
                  ] } } }
                ]
                """,
        });

        var result = (await TimelineTools.GetTimeline("person", "anchor", client: CreateClient(handler)))
            .Replace("\r\n", "\n");

        Assert.Contains(
            "  1820: Birth  [event: own-birth]\n" +
            "  1856-08-01: Birth [Father], age 36 years  [event: kid-birth]\n" +
            "    Participants: Ivanova, Evdokia (I0203), Ivanova, Anastasia (I0202) [Mother]\n" +
            "  1858-10-27: Marriage [Witness]  [event: wedding]\n" +
            "    Participants: Petrov, Ivan (I0301) [Groom], Petrova, Maria (I0302) [Bride], Petrov, Ivan and Petrova, Maria (F0060)\n" +
            "    Church record\n" +
            "  1860: Census [Informant]  [event: census]\n" +
            "  1874: Marriage (Son): Ivanov, Larion (I0167) [Family]  [event: son-wedding]\n",
            result);
        Assert.Equal(
            [
                "/api/people/anchor/timeline?discard_empty=false",
                "/api/events/?handles=kid-birth,wedding,census&profile=participants&page=1&pagesize=3",
            ],
            handler.Requests);
    }

    [Fact]
    public async Task ExtendedPerson_Names_The_Others_In_Events_Under_Another_Role()
    {
        var handler = new PathHandler(new Dictionary<string, string>
        {
            ["/api/events/kid-birth?profile=participants"] =
                $$"""{ "handle": "kid-birth", "type": "Birth", "profile": {{KidBirthParticipants}} }""",
        });
        var person = JsonSerializer.Deserialize<GrampsPersonExtended>("""
            {"handle":"anchor","gramps_id":"I0113","birth_ref_index":0,
             "event_ref_list":[{"ref":"own-birth","role":"Primary"},{"ref":"kid-birth","role":"Father"}],
             "extended":{"events":[
               {"handle":"own-birth","type":"Birth","date":{"modifier":0,"dateval":[0,0,1820,false]}},
               {"handle":"kid-birth","type":"Birth","date":{"modifier":0,"dateval":[1,8,1856,false]},"description":"Baptised"}]}}
            """, GrampsJson.Options)!;

        var result = (await PersonFormatter.FormatPersonExtended(person, CreateClient(handler))).Replace("\r\n", "\n");

        Assert.Contains(
            "  • Birth: 1820 [Primary] [handle: own-birth]\n" +
            "  • Birth: 1856-08-01 [Father] [handle: kid-birth]\n" +
            "    Participants: Ivanova, Evdokia (I0203), Ivanova, Anastasia (I0202) [Mother]\n" +
            "    Baptised\n",
            result);
        Assert.Equal(
            ["/api/events/kid-birth?profile=participants"],
            handler.Requests.Where(p => p.Contains("participants", StringComparison.Ordinal)));
    }

    private static GrampsApiClient CreateClient(HttpMessageHandler handler)
    {
        var config = new GrampsConfig("https://gramps-web.test", "user", "pass", "tree");
        var http = new HttpClient(handler, disposeHandler: false);
        return new GrampsApiClient(http, config, NullLogger<GrampsApiClient>.Instance,
            new GrampsAuthTokenProvider(http, config, NullLogger<GrampsAuthTokenProvider>.Instance));
    }

    /// <summary>Serves bodies by path and query, empty type lists, and 404 otherwise.</summary>
    private sealed class PathHandler(IReadOnlyDictionary<string, string> bodies) : HttpMessageHandler
    {
        private readonly ConcurrentQueue<string> _requests = new();

        public IReadOnlyList<string> Requests => [.. _requests];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.PathAndQuery;
            if (path.StartsWith("/api/token/", StringComparison.Ordinal))
                return Task.FromResult(Json("""{"access_token":"token","refresh_token":"refresh","expires_in":900}"""));
            if (path.StartsWith("/api/types/", StringComparison.Ordinal))
                return Task.FromResult(Json("[]"));

            _requests.Enqueue(path);
            return Task.FromResult(bodies.TryGetValue(path, out var body)
                ? Json(body)
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }
}
