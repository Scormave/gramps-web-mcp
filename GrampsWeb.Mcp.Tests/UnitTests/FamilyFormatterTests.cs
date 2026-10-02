using System.Net;
using System.Text;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Config;
using GrampsWeb.Mcp.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

[Collection("HandleCache")]
public class FamilyFormatterTests
{
    private const string FamilyWithProfile = """
        {
          "handle": "fam-h",
          "gramps_id": "F0001",
          "father_handle": "father-h",
          "mother_handle": null,
          "type": "Married",
          "child_ref_list": [
            { "ref": "gone-h", "frel": "Birth", "mrel": "Birth" },
            { "ref": "son-h", "frel": "Adopted", "mrel": "Birth" }
          ],
          "event_ref_list": [{ "ref": "marr-h", "role": "Family" }],
          "profile": {
            "handle": "fam-h",
            "gramps_id": "F0001",
            "father": {
              "handle": "father-h", "gramps_id": "I0001", "name_display": "Petrov, Ivan",
              "birth": { "type": "Birth", "date": "1880", "place_name": "Dublin" },
              "death": { "type": "Burial", "date": "1950", "place_name": "" }
            },
            "mother": {},
            "marriage": { "type": "Marriage", "date": "1905-06-01", "place_name": "Cork" },
            "divorce": {},
            "children": [
              {},
              { "handle": "son-h", "gramps_id": "I0003", "name_display": "Petrov, Oleg", "birth": {}, "death": {} }
            ],
            "events": [{ "type": "Marriage", "date": "1905-06-01", "place_name": "Cork" }]
          }
        }
        """;

    [Fact]
    public async Task ReadFamily_Names_Members_And_Events_From_One_Profile_Request()
    {
        var handler = new FamilyHandler(FamilyWithProfile);

        var result = (await FamilyTools.ReadFamilyAsync("fam-h", client: CreateClient(handler))).Replace("\r\n", "\n");

        Assert.Equal(["/api/families/fam-h?profile=self,events"], handler.FamilyRequests);
        Assert.Contains("Father: Petrov, Ivan (I0001) [handle: father-h], b. 1880 in Dublin, burial 1950\n", result);
        Assert.DoesNotContain("Mother:", result);
        Assert.Contains("Marriage: 1905-06-01 in Cork\n", result);
        Assert.DoesNotContain("Divorce:", result);
        Assert.Contains("  • [handle: gone-h] | frel: Birth, mrel: Birth\n", result);
        Assert.Contains("  • Petrov, Oleg (I0003) [handle: son-h] | frel: Adopted, mrel: Birth\n", result);
        Assert.Contains("  • Marriage 1905-06-01 in Cork [handle: marr-h] role: Family\n", result);
    }

    [Fact]
    public async Task ReadFamily_Without_Profile_Shows_Handles_And_Skips_Missing_Parent()
    {
        const string json = """
            {
              "handle": "fam-h",
              "gramps_id": "F0001",
              "father_handle": "",
              "mother_handle": "mother-h",
              "child_ref_list": [{ "ref": "son-h" }],
              "event_ref_list": [{ "ref": "marr-h" }]
            }
            """;

        var result = await FamilyTools.ReadFamilyAsync("fam-h", client: CreateClient(new FamilyHandler(json)));

        Assert.DoesNotContain("Father:", result);
        Assert.Contains("Mother: [handle: mother-h]", result);
        Assert.Contains("  • [handle: son-h] | frel: Birth, mrel: Birth", result);
        Assert.Contains("  • [handle: marr-h] role: Primary", result);
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

    private sealed class FamilyHandler(string familyJson) : HttpMessageHandler
    {
        public List<string> FamilyRequests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Post && path == "/api/token/")
                return Task.FromResult(Json("""{"access":"tok","refresh":"ref"}"""));
            if (path == "/api/families/fam-h")
            {
                FamilyRequests.Add(request.RequestUri.PathAndQuery);
                return Task.FromResult(Json(familyJson));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }
}
