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

public class PlaceFormatterTests
{
    [Fact]
    public async Task FormatPlaceFull_Shows_Hierarchy_And_AlternateNames()
    {
        var boise = new GrampsPlace
        {
            Handle = "boise-h",
            GrampsId = "P1",
            Name = "Boise",
            Type = "City",
            PlaceRefList = [new GrampsPlaceRef { Ref = "idaho-h", Date = GrampsDate.Range(1900, 1950) }],
            AlternateNames =
            [
                new GrampsPlaceName { Value = "Boise City", Lang = "en" }
            ]
        };

        var handler = new PlaceHandler(new Dictionary<string, string>
        {
            ["idaho-h"] = """
                {
                  "handle": "idaho-h",
                  "name": { "value": "Idaho", "lang": "en" },
                  "place_type": "State",
                  "placeref_list": [{ "ref": "usa-h" }]
                }
                """,
            ["usa-h"] = """
                {
                  "handle": "usa-h",
                  "name": { "value": "United States", "lang": "en" },
                  "place_type": "Country",
                  "placeref_list": []
                }
                """
        });

        var client = CreateClient(handler);
        var result = await PlaceFormatter.FormatPlaceFull(boise, client);

        Assert.Contains("Hierarchy:", result);
        Assert.Contains("PLACE: Boise", result);
        Assert.Contains("[handle: boise-h]", result);
        Assert.DoesNotContain("Hierarchy: Boise", result);
        Assert.DoesNotContain("[boise-h]", GetHierarchyLine(result));
        Assert.Contains("Idaho", result);
        Assert.Contains("[idaho-h]", result);
        Assert.Contains("United States", result);
        Assert.Contains("Enclosed by:", result);
        Assert.Contains("idaho-h", result);
        Assert.Contains("between 1900 and 1950", result);
        Assert.Contains("Alternate names (1):", result);
        Assert.Contains("Boise City", result);
        Assert.Contains("(en)", result);
    }

    [Fact]
    public async Task FormatPlaceFull_Omits_Empty_Enclosure_And_AltName_Dates()
    {
        var place = new GrampsPlace
        {
            Handle = "city-h",
            GrampsId = "P2",
            Name = "City",
            Type = "City",
            PlaceRefList =
            [
                new GrampsPlaceRef { Ref = "parent-h", Date = new GrampsDate() },
                new GrampsPlaceRef { Ref = "other-h", Date = new GrampsDate { Text = "" } }
            ],
            AlternateNames =
            [
                new GrampsPlaceName { Value = "Old", Date = new GrampsDate { Text = "" } }
            ]
        };

        var handler = new PlaceHandler(new Dictionary<string, string>
        {
            ["parent-h"] = """
                {
                  "handle": "parent-h",
                  "name": { "value": "Parent", "lang": "en" },
                  "place_type": "State",
                  "placeref_list": []
                }
                """,
            ["other-h"] = """
                {
                  "handle": "other-h",
                  "name": { "value": "Other", "lang": "en" },
                  "place_type": "State",
                  "placeref_list": []
                }
                """
        });

        var client = CreateClient(handler);
        var result = await PlaceFormatter.FormatPlaceFull(place, client);

        Assert.Contains("Hierarchy: Parent [parent-h]", result);
        Assert.DoesNotContain("[]", result);
        Assert.Contains("  - parent-h", result);
        Assert.Contains("  - other-h", result);
        Assert.Contains("  - Old", result);
        Assert.DoesNotContain("Old [", result);
    }

    [Fact]
    public async Task FormatPlaceFull_Names_Parents_From_Profile_Without_Requests()
    {
        const string json = """
            {
              "handle": "boise-h",
              "gramps_id": "P1",
              "name": { "value": "Boise" },
              "place_type": "City",
              "placeref_list": [
                { "ref": "idaho-h", "date": { "modifier": 4, "dateval": [0, 0, 1900, false, 0, 0, 1950, false] } },
                { "ref": "ada-h" }
              ],
              "profile": {
                "gramps_id": "P1",
                "name": "Boise",
                "type": "City",
                "parent_places": [
                  { "gramps_id": "P2", "name": "Idaho", "type": "State" },
                  { "gramps_id": "P3", "name": "United States", "type": "Country" }
                ],
                "direct_parent_places": [
                  { "place": { "gramps_id": "P2", "name": "Idaho", "type": "State" }, "date_str": "between 1900 and 1950" },
                  { "place": { "gramps_id": "P4", "name": "Ada", "type": "County" }, "date_str": "" }
                ]
              }
            }
            """;
        var place = JsonSerializer.Deserialize<GrampsPlace>(json, GrampsJson.Options)!;
        var handler = new PlaceHandler([]);

        var result = await PlaceFormatter.FormatPlaceFull(place, CreateClient(handler));

        Assert.Equal(0, handler.PlaceRequests);
        Assert.Equal("Hierarchy: Idaho (State, P2), United States (Country, P3)", GetHierarchyLine(result).TrimEnd('\r'));
        Assert.Contains("  - Idaho (State, P2) [handle: idaho-h] [between 1900 and 1950]", result);
        Assert.Contains("  - Ada (County, P4) [handle: ada-h]", result);
    }

    [Fact]
    public async Task FormatPlaceFull_Falls_Back_To_Handles_When_Profile_Skips_A_Missing_Parent()
    {
        var place = new GrampsPlace
        {
            Handle = "city-h",
            Name = "City",
            PlaceRefList = [new GrampsPlaceRef { Ref = "gone-h" }, new GrampsPlaceRef { Ref = "ada-h" }],
            Profile = new GrampsPlaceProfile
            {
                ParentPlaces = [],
                DirectParentPlaces = [new GrampsDirectParentPlace { Place = new GrampsPlaceProfile { Name = "Ada" } }]
            }
        };

        var result = await PlaceFormatter.FormatPlaceFull(place, CreateClient(new PlaceHandler([])));

        Assert.Contains("  - gone-h", result);
        Assert.Contains("  - ada-h", result);
        Assert.DoesNotContain("Ada", result);
    }

    private static string GetHierarchyLine(string result)
    {
        foreach (var line in result.Split('\n'))
        {
            if (line.StartsWith("Hierarchy:", StringComparison.Ordinal))
                return line;
        }

        return string.Empty;
    }

    [Fact]
    public void Deserialize_PlaceRefList_As_Objects_Parses_Ref_And_Date()
    {
        const string json = """
            {
              "handle": "H1",
              "name": "Boise",
              "placeref_list": [
                { "ref": "PARENT1", "date": { "modifier": 4, "dateval": [0, 0, 1920, false, 0, 0, 1950, false] } }
              ],
              "alt_names": [
                { "value": "Old Boise", "lang": "en", "date": { "modifier": 0, "dateval": [0, 0, 1900, false] } }
              ]
            }
            """;

        var place = JsonSerializer.Deserialize<GrampsPlace>(json, GrampsJson.Options);
        Assert.NotNull(place);
        Assert.Single(place!.PlaceRefList!);
        Assert.Equal("PARENT1", place.PlaceRefList![0].Ref);
        Assert.Equal(1920, place.PlaceRefList[0].Date!.Year);
        Assert.Single(place.AlternateNames!);
        Assert.Equal("Old Boise", place.AlternateNames![0].Value);
        Assert.Equal("en", place.AlternateNames[0].Lang);
        Assert.Equal(1900, place.AlternateNames[0].Date!.Year);
    }

    private static GrampsApiClient CreateClient(HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://gramps-web.test") };
        var config = new GrampsConfig("https://gramps-web.test", "user", "pass", "tree");
        var tokenProvider = new GrampsAuthTokenProvider(
            new HttpClient(handler),
            config,
            NullLogger<GrampsAuthTokenProvider>.Instance);

        return new GrampsApiClient(
            httpClient,
            config,
            NullLogger<GrampsApiClient>.Instance,
            tokenProvider);
    }

    private sealed class PlaceHandler(Dictionary<string, string> placesByHandle) : HttpMessageHandler
    {
        public int PlaceRequests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Post && request.RequestUri?.AbsolutePath == "/api/token/")
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"access":"tok","refresh":"ref"}""", Encoding.UTF8, "application/json")
                });
            }

            var path = request.RequestUri?.AbsolutePath ?? "";
            if (path.StartsWith("/api/places/", StringComparison.Ordinal))
            {
                PlaceRequests++;
                var handle = Uri.UnescapeDataString(path["/api/places/".Length..]);
                if (placesByHandle.TryGetValue(handle, out var json))
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(json, Encoding.UTF8, "application/json")
                    });
                }
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
