using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Config;
using GrampsWeb.Mcp.Input;
using GrampsWeb.Mcp.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

/// <summary>
/// The write tools check name types, surname origins, attribute, URL, child relation and repository media types, and
/// send each in the spelling of the tree's vocabulary.
/// </summary>
public class KnownTypeTests
{
    [Fact]
    public async Task CreatePerson_Stores_Name_Types_And_Origins_In_Their_Known_Spelling()
    {
        var handler = new TreeHandler(new Dictionary<string, string>());

        await PersonTools.CreatePerson(
            Name("""{ "given": "Ivan", "surname": "Petrov", "type": "married name", "origin_type": "unknown" }"""),
            alternateNames: Deserialize<FlexibleAlternateNameList>("""["also known as:: Ivan|Petrov"]"""),
            client: CreateClient(handler));

        var primary = handler.Body!["primary_name"]!;
        Assert.Equal("Married Name", primary["type"]!.GetValue<string>());
        // Gramps spells the unknown origin with a trailing space and reads anything else as a new custom origin.
        Assert.Equal("Unknown ", primary["surname_list"]![0]!["origintype"]!.GetValue<string>());
        Assert.Equal("Also Known As", handler.Body["alternate_names"]![0]!["type"]!.GetValue<string>());
    }

    [Fact]
    public async Task CreatePerson_Rejects_An_Origin_Gramps_Would_Store_As_A_New_Custom_One()
    {
        var handler = new TreeHandler(new Dictionary<string, string>());

        var error = await Assert.ThrowsAsync<McpException>(() => PersonTools.CreatePerson(
            Name("""{ "given": "Ivan", "surname": "Petrov", "origin_type": "Custom" }"""), client: CreateClient(handler)));

        Assert.StartsWith("primaryName: Invalid name origin types 'Custom'.", error.Message);
        Assert.Null(handler.Body);
    }

    [Fact]
    public async Task CreatePerson_Stores_Attribute_And_Url_Types_In_Their_Known_Spelling()
    {
        var handler = new TreeHandler(new Dictionary<string, string>());

        await PersonTools.CreatePerson(
            Name(),
            attributes: Deserialize<FlexibleAttributeList>("""["nationality: Polish", "nickname: Vanya"]"""),
            urls: Deserialize<FlexibleUrlList>("""[{ "type": "web home", "path": "https://example.org" }]"""),
            client: CreateClient(handler));

        Assert.Equal(
            ["Nationality", "Nickname"],
            handler.Body!["attribute_list"]!.AsArray().Select(a => a!["type"]!.GetValue<string>()));
        Assert.Equal("Web Home", handler.Body["urls"]![0]!["type"]!.GetValue<string>());
    }

    [Fact]
    public async Task CreatePerson_Rejects_A_Misspelt_Attribute_Type_Before_Saving()
    {
        var handler = new TreeHandler(new Dictionary<string, string>());

        var error = await Assert.ThrowsAsync<McpException>(() => PersonTools.CreatePerson(
            Name(), attributes: Deserialize<FlexibleAttributeList>("""["Nationalty: Polish"]"""), client: CreateClient(handler)));

        Assert.StartsWith("attributes: Invalid attribute types 'Nationalty'.", error.Message);
        Assert.Contains("Nationality", error.Message);
        Assert.Null(handler.Body);
    }

    [Fact]
    public async Task UpdatePerson_Rejects_A_Misspelt_Url_Type_Without_Reading_Or_Saving_The_Person()
    {
        var handler = new TreeHandler(new Dictionary<string, string>());

        var error = await Assert.ThrowsAsync<McpException>(() => PersonTools.UpdatePerson(
            "h-person",
            urls: Deserialize<FlexibleUrlList>("""[{ "type": "Web Hom", "path": "https://example.org" }]"""),
            client: CreateClient(handler)));

        Assert.StartsWith("urls: Invalid url types 'Web Hom'. Did you mean: Web Home", error.Message);
        Assert.DoesNotContain(handler.Requests, path => path.StartsWith("/api/people/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CreateFamily_Accepts_A_Custom_Attribute_Type_The_Tree_Uses_On_People()
    {
        var handler = new TreeHandler(new Dictionary<string, string>());

        await FamilyTools.CreateFamily(
            attributes: Deserialize<FlexibleAttributeList>("""["patronymic: Ivanovich"]"""), client: CreateClient(handler));

        Assert.Equal("Patronymic", handler.Body!["attribute_list"]![0]!["type"]!.GetValue<string>());
    }

    [Fact]
    public async Task CreateFamily_Stores_Child_Relations_In_Their_Known_Spelling()
    {
        var handler = new TreeHandler(new Dictionary<string, string>());

        await FamilyTools.CreateFamily(
            childRefs: Deserialize<FlexibleChildRefList>("""["h-child::adopted"]"""), client: CreateClient(handler));

        var childRef = handler.Body!["child_ref_list"]![0]!;
        Assert.Equal("Adopted", childRef["frel"]!.GetValue<string>());
        Assert.Equal("Adopted", childRef["mrel"]!.GetValue<string>());
    }

    [Fact]
    public async Task CreateFamily_Rejects_A_Misspelt_Child_Relation_Before_Saving()
    {
        var handler = new TreeHandler(new Dictionary<string, string>());

        var error = await Assert.ThrowsAsync<McpException>(() => FamilyTools.CreateFamily(
            childRefs: Deserialize<FlexibleChildRefList>("""["h-child::Adoptd"]"""), client: CreateClient(handler)));

        Assert.StartsWith("childRefs: Invalid child reference types 'Adoptd'. Did you mean: Adopted?", error.Message);
        Assert.Null(handler.Body);
    }

    [Fact]
    public async Task UpdateFamily_Remove_Does_Not_Check_Child_Relations()
    {
        var handler = new TreeHandler(new Dictionary<string, string>
        {
            ["/api/families/h-family"] = """
                {
                  "handle": "h-family", "gramps_id": "F0100",
                  "child_ref_list": [{ "ref": "h-child", "frel": "Birth", "mrel": "Birth" }, { "ref": "h-old", "frel": "Birth", "mrel": "Birth" }]
                }
                """
        });

        await FamilyTools.UpdateFamily(
            "h-family", childRefs: Deserialize<FlexibleChildRefList>("""["h-old::Anything"]"""),
            client: CreateClient(handler), linkMode: "remove");

        Assert.DoesNotContain(handler.Requests, path => path.StartsWith("/api/types/", StringComparison.Ordinal));
        Assert.Equal(["h-child"], handler.Body!["child_ref_list"]!.AsArray().Select(r => r!["ref"]!.GetValue<string>()));
    }

    [Fact]
    public async Task CreateEvent_Stores_Attribute_Types_In_Their_Known_Spelling()
    {
        var handler = new TreeHandler(new Dictionary<string, string>());

        await EventTools.CreateEvent(
            "Birth", attributes: Deserialize<FlexibleAttributeList>("""["father age: 34"]"""), client: CreateClient(handler));

        Assert.Equal("Father Age", handler.Body!["attribute_list"]![0]!["type"]!.GetValue<string>());
    }

    [Fact]
    public async Task CreateSource_Stores_Source_Attribute_And_Media_Types_In_Their_Known_Spelling()
    {
        var handler = new TreeHandler(new Dictionary<string, string>());

        await SourceTools.CreateSource(
            "Metrical book",
            repositoryHandles: Deserialize<FlexibleRepositoryRefList>("""[{ "ref": "h-repo", "media_type": "book" }]"""),
            attributes: Deserialize<FlexibleAttributeList>("""["status: draft"]"""),
            client: CreateClient(handler));

        Assert.Equal("Book", handler.Body!["reporef_list"]![0]!["media_type"]!.GetValue<string>());
        Assert.Equal("Status", handler.Body["attribute_list"]![0]!["type"]!.GetValue<string>());
    }

    [Fact]
    public async Task UpdateCitation_Rejects_A_Person_Attribute_Type_Without_Reading_Or_Saving_The_Citation()
    {
        var handler = new TreeHandler(new Dictionary<string, string>());

        var error = await Assert.ThrowsAsync<McpException>(() => CitationTools.UpdateCitation(
            "h-citation", attributes: Deserialize<FlexibleAttributeList>("""["Nationality: Polish"]"""),
            client: CreateClient(handler)));

        Assert.StartsWith("attributes: Invalid source attribute types 'Nationality'.", error.Message);
        Assert.DoesNotContain(handler.Requests, path => path.StartsWith("/api/citations/", StringComparison.Ordinal));
    }

    private static FlexibleGrampsName Name(string json = """{ "given": "Ivan", "surname": "Petrov" }""") =>
        Deserialize<FlexibleGrampsName>(json);

    private static T Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json)!;

    private static GrampsApiClient CreateClient(HttpMessageHandler handler)
    {
        var config = new GrampsConfig("https://known-types.test", "user", "pass", "tree");
        var http = new HttpClient(handler);
        var tokens = new GrampsAuthTokenProvider(http, config, NullLogger<GrampsAuthTokenProvider>.Instance);
        return new GrampsApiClient(http, config, NullLogger<GrampsApiClient>.Instance, tokens);
    }

    /// <summary>
    /// Serves the type vocabularies (two custom person attribute types) and bodies by path and query (404 otherwise),
    /// and records the POST or PUT body.
    /// </summary>
    private sealed class TreeHandler(IReadOnlyDictionary<string, string> bodies) : HttpMessageHandler
    {
        private const string DefaultTypes = """
            {
              "name_types": ["Unknown", "Also Known As", "Birth Name", "Married Name"],
              "name_origin_types": ["", "Unknown ", "Inherited", "Given", "Taken", "Patronymic", "Matronymic", "Feudal",
                                    "Pseudonym", "Patrilineal", "Matrilineal", "Occupation", "Location"],
              "attribute_types": ["Unknown", "Caste", "Description", "Identification Number", "National Origin",
                                  "Number of Children", "Social Security Number", "Nickname", "Cause", "Agency", "Age",
                                  "Father Age", "Mother Age", "Witness", "Time", "Occupation"],
              "source_attribute_types": ["Unknown", "Status", "Identification Number", "Date", "Priority", "Page"],
              "url_types": ["Unknown", "E-mail", "Web Home", "Web Search", "FTP"],
              "child_reference_types": ["None", "Birth", "Adopted", "Stepchild", "Sponsored", "Foster", "Unknown"],
              "source_media_types": ["Unknown", "Audio", "Book", "Card", "Electronic", "Fiche", "Film", "Magazine",
                                     "Manuscript", "Map", "Newspaper", "Photo", "Tombstone", "Video"]
            }
            """;

        private readonly ConcurrentQueue<string> _requests = new();

        public IReadOnlyList<string> Requests => [.. _requests];

        public JsonNode? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.PathAndQuery;
            if (path.StartsWith("/api/token/", StringComparison.Ordinal))
                return Json("""{"access_token":"token","refresh_token":"refresh","expires_in":900}""");

            _requests.Enqueue(path);
            if (request.Method == HttpMethod.Post || request.Method == HttpMethod.Put)
            {
                Body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                return Json(request.Method == HttpMethod.Put
                    ? "{}"
                    : """[{ "_class": "Person", "type": "add", "old": null, "new": { "_class": "Person", "handle": "h-new", "gramps_id": "I0001" } }]""");
            }

            return path switch
            {
                "/api/types/default/" => Json(DefaultTypes),
                "/api/types/custom/" => Json("""{ "person_attribute_types": ["Nationality", "Patronymic"] }"""),
                _ => bodies.TryGetValue(path, out var body) ? Json(body) : new HttpResponseMessage(HttpStatusCode.NotFound)
            };
        }

        private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }
}
