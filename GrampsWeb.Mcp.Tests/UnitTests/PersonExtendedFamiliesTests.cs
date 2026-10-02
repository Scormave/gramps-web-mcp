using System.Net;
using System.Text;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Config;
using GrampsWeb.Mcp.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

[Collection("HandleCache")]
public class PersonExtendedFamiliesTests
{
    private const string Extended = """
        "extended": {
          "parent_families": [
            { "handle": "parents-h", "gramps_id": "F0001", "father_handle": "father-h", "mother_handle": "" },
            { "handle": "foster-h", "father_handle": "foster-father-h", "mother_handle": "foster-mother-h" }
          ],
          "families": [
            {
              "handle": "marr-h", "gramps_id": "F0002", "father_handle": "husband-h", "mother_handle": "anna-h",
              "type": "Married", "child_ref_list": [{ "ref": "son-h" }, { "ref": "daughter-h" }]
            },
            { "handle": "single-h", "gramps_id": "F0003", "mother_handle": "anna-h", "type": "Unknown", "child_ref_list": [] }
          ]
        }
        """;

    private const string PersonWithProfile = $$"""
        {
          "handle": "anna-h",
          "gramps_id": "I0002",
          "gender": 0,
          "parent_family_list": ["parents-h", "foster-h"],
          "family_list": ["marr-h", "single-h"],
          {{Extended}},
          "profile": {
            "handle": "anna-h",
            "gramps_id": "I0002",
            "primary_parent_family": {
              "handle": "parents-h",
              "gramps_id": "F0001",
              "father": {
                "handle": "father-h", "gramps_id": "I0001", "name_display": "Petrov, Ivan",
                "birth": { "type": "Birth", "date": "1880", "place_name": "Dublin" }, "death": {}
              },
              "mother": {}
            },
            "other_parent_families": [
              { "handle": "foster-h", "father": { "handle": "foster-father-h", "gramps_id": "I0010" }, "mother": {} }
            ],
            "families": [
              {
                "handle": "marr-h",
                "father": {
                  "handle": "husband-h", "gramps_id": "I0004", "name_display": "Sidorov, Pavel",
                  "birth": { "type": "Birth", "date": "1878" }, "death": {}
                },
                "mother": { "handle": "anna-h", "gramps_id": "I0002", "name_display": "Petrova, Anna" },
                "marriage": { "type": "Marriage", "date": "1905-06-01", "place_name": "Cork" },
                "divorce": { "type": "Divorce", "date": "1920" },
                "children": [
                  { "handle": "son-h", "gramps_id": "I0005", "name_display": "Sidorov, Oleg", "birth": { "date": "1906" } },
                  {}
                ]
              },
              { "handle": "single-h", "father": {}, "mother": {}, "marriage": {}, "divorce": {}, "children": [] }
            ]
          }
        }
        """;

    [Fact]
    public async Task ReadPersonExtended_Names_Families_From_The_Same_Request()
    {
        var handler = new PersonHandler(PersonWithProfile);

        var result = (await PersonTools.ReadPersonAsync("anna-h", extended: true, client: CreateClient(handler)))
            .Replace("\r\n", "\n");

        Assert.Equal(["/api/people/anna-h?extend=all&profile=families"], handler.PersonRequests);
        Assert.Contains("""
              Parent families (as child) (2):
              • F0001 [handle: parents-h]
                Father: Petrov, Ivan (I0001) [handle: father-h], b. 1880 in Dublin
                Mother: —
              • [handle: foster-h]
                Father: I0010 [handle: foster-father-h]
                Mother: [handle: foster-mother-h]

              Families as parent or spouse (2):
              • [Married] F0002 [handle: marr-h]
                Spouse: Sidorov, Pavel (I0004) [handle: husband-h], b. 1878
                Marriage: 1905-06-01 in Cork
                Divorce: 1920
                Children (2):
                  • Sidorov, Oleg (I0005) [handle: son-h], b. 1906
                  • [handle: daughter-h]
              • [Unknown] F0003 [handle: single-h]
                Spouse: —
                Children: none

            """.Replace("\r\n", "\n"), result);
    }

    [Fact]
    public async Task ReadPersonExtended_Without_Profile_Shows_Family_Members_By_Handle()
    {
        var handler = new PersonHandler($$"""{ "handle": "anna-h", "gramps_id": "I0002", {{Extended}} }""");

        var result = (await PersonTools.ReadPersonAsync("anna-h", extended: true, client: CreateClient(handler)))
            .Replace("\r\n", "\n");

        Assert.Contains("""
              • F0001 [handle: parents-h]
                Father: [handle: father-h]
                Mother: —
            """.Replace("\r\n", "\n"), result);
        Assert.Contains("""
              • [Married] F0002 [handle: marr-h]
                Spouse: [handle: husband-h]
                Children (2):
                  • [handle: son-h]
                  • [handle: daughter-h]
            """.Replace("\r\n", "\n"), result);
        Assert.DoesNotContain("Marriage:", result);
    }

    [Theory]
    [InlineData(false, "/api/people/anna-h?profile=self", "PERSON: ")]
    [InlineData(true, "/api/people/anna-h?extend=all&profile=families", "Person (extended): ")]
    public async Task ReadPerson_Header_Uses_The_Tree_Name_Display_Format(bool extended, string request, string header)
    {
        var handler = new PersonHandler("""
            {
              "handle": "anna-h", "gramps_id": "I0002", "gender": 0,
              "primary_name": { "first_name": "Anna Ivanovna", "surname_list": [{ "surname": "Petrova", "primary": true }] },
              "profile": { "handle": "anna-h", "gramps_id": "I0002", "name_display": "Petrova, Anna Ivanovna" }
            }
            """);

        var result = (await PersonTools.ReadPersonAsync("anna-h", extended, client: CreateClient(handler)))
            .Replace("\r\n", "\n");

        Assert.Equal([request], handler.PersonRequests);
        Assert.StartsWith($"{header}Petrova, Anna Ivanovna [handle: anna-h] (gramps_id: I0002)\n", result);
        // The primary name line spells the name out as stored, like the alternate names.
        Assert.Contains("]: Anna Ivanovna Petrova\n", result);
    }

    private static GrampsApiClient CreateClient(HttpMessageHandler handler)
    {
        var config = new GrampsConfig("https://gramps-web.test", "user", "pass", "tree");
        var tokenProvider = new GrampsAuthTokenProvider(
            new HttpClient(handler),
            config,
            NullLogger<GrampsAuthTokenProvider>.Instance);
        return new GrampsApiClient(
            new HttpClient(handler) { BaseAddress = new Uri("https://gramps-web.test") },
            config,
            NullLogger<GrampsApiClient>.Instance,
            tokenProvider);
    }

    private sealed class PersonHandler(string personJson) : HttpMessageHandler
    {
        public List<string> PersonRequests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Post && path == "/api/token/")
                return Task.FromResult(Json("""{"access":"tok","refresh":"ref"}"""));
            if (path is "/api/types/default/" or "/api/types/custom/")
                return Task.FromResult(Json("{}"));
            if (path.StartsWith("/api/types/default/", StringComparison.Ordinal))
                return Task.FromResult(Json("[]"));
            if (path == "/api/people/anna-h")
            {
                PersonRequests.Add(request.RequestUri.PathAndQuery);
                return Task.FromResult(Json(personJson));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }
}
