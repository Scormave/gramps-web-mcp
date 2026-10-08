using System.Net;
using System.Text;
using System.Text.Json;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Config;
using GrampsWeb.Mcp.Input;
using GrampsWeb.Mcp.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

public class PlaceToolsTests
{
    private const string StoredPlace = """
        {
          "handle": "place-h",
          "gramps_id": "P0007",
          "name": {
            "value": "Pushkin",
            "lang": "ru",
            "date": { "modifier": 7, "dateval": [10, 2, 1937, false], "sortval": 2428575 }
          },
          "place_type": "City",
          "lat": "59.7225",
          "long": "30.4167"
        }
        """;

    [Fact]
    public async Task CreatePlace_Sends_Primary_Name_Date()
    {
        var handler = new PlaceHandler();

        await PlaceTools.CreatePlace("Pushkin", nameLang: "ru", nameDate: "from 1937-02-10", client: CreateClient(handler));

        var name = handler.Body!.Value.GetProperty("name");
        Assert.Equal("Pushkin", name.GetProperty("value").GetString());
        Assert.Equal("ru", name.GetProperty("lang").GetString());
        AssertDate(name.GetProperty("date"), modifier: 7, day: 10, month: 2, year: 1937);
    }

    [Fact]
    public async Task CreatePlace_Without_Name_Date_Sends_Undated_Name()
    {
        var handler = new PlaceHandler();

        await PlaceTools.CreatePlace("Pushkin", client: CreateClient(handler));

        Assert.False(handler.Body!.Value.GetProperty("name").TryGetProperty("date", out _));
    }

    [Fact]
    public async Task CreatePlace_Rejects_Unrecognized_Name_Date_Before_Posting()
    {
        var handler = new PlaceHandler();

        await Assert.ThrowsAsync<McpException>(() =>
            PlaceTools.CreatePlace("Pushkin", nameDate: "since the revolution", client: CreateClient(handler)));

        Assert.Null(handler.Body);
    }

    [Fact]
    public async Task UpdatePlace_Sets_Primary_Name_Date_And_Keeps_Value_And_Lang()
    {
        var handler = new PlaceHandler(StoredPlace);

        await PlaceTools.UpdatePlace("place-h", nameDate: "1918-1937-02-10", client: CreateClient(handler));

        var name = handler.Body!.Value.GetProperty("name");
        Assert.Equal("Pushkin", name.GetProperty("value").GetString());
        Assert.Equal("ru", name.GetProperty("lang").GetString());
        var date = name.GetProperty("date");
        Assert.Equal(5, date.GetProperty("modifier").GetInt32());
        Assert.Equal(1918, date.GetProperty("dateval")[2].GetInt32());
        Assert.Equal(1937, date.GetProperty("dateval")[6].GetInt32());
    }

    [Fact]
    public async Task UpdatePlace_Keeps_Primary_Name_Date_When_Omitted()
    {
        var handler = new PlaceHandler(StoredPlace);

        await PlaceTools.UpdatePlace("place-h", name: "Pushkin City", client: CreateClient(handler));

        var name = handler.Body!.Value.GetProperty("name");
        Assert.Equal("Pushkin City", name.GetProperty("value").GetString());
        Assert.Equal("ru", name.GetProperty("lang").GetString());
        AssertDate(name.GetProperty("date"), modifier: 7, day: 10, month: 2, year: 1937);
    }

    [Fact]
    public async Task UpdatePlace_Empty_Name_Date_Removes_It()
    {
        var handler = new PlaceHandler(StoredPlace);

        await PlaceTools.UpdatePlace("place-h", nameDate: "", client: CreateClient(handler));

        var name = handler.Body!.Value.GetProperty("name");
        Assert.Equal("ru", name.GetProperty("lang").GetString());
        Assert.False(name.TryGetProperty("date", out _));
    }

    [Fact]
    public async Task CreatePlace_Sends_Numeric_Coordinates_As_Text()
    {
        var handler = new PlaceHandler();

        await PlaceTools.CreatePlace(
            "London",
            lat: JsonSerializer.Deserialize<FlexibleString>("51.5072"),
            lon: JsonSerializer.Deserialize<FlexibleString>("-0.1276"),
            client: CreateClient(handler));

        Assert.Equal(JsonValueKind.String, handler.Body!.Value.GetProperty("lat").ValueKind);
        Assert.Equal("51.5072", handler.Body!.Value.GetProperty("lat").GetString());
        Assert.Equal("-0.1276", handler.Body!.Value.GetProperty("long").GetString());
    }

    [Theory]
    [InlineData("\"52.2297\"", "52.2297")]
    [InlineData(" 52.2297 ", "52.2297")]
    [InlineData("\" +52.2297 \"", "+52.2297")]
    [InlineData("52°13'47\"N", "52°13'47\"N")]
    public async Task CreatePlace_Trims_Coordinates_And_Drops_Wrapping_Quotes(string lat, string stored)
    {
        var handler = new PlaceHandler();

        await PlaceTools.CreatePlace("Warsaw", lat: new FlexibleString { Value = lat }, client: CreateClient(handler));

        Assert.Equal(stored, handler.Body!.Value.GetProperty("lat").GetString());
    }

    [Fact]
    public async Task UpdatePlace_Sets_Numeric_Latitude_And_Keeps_Longitude()
    {
        var handler = new PlaceHandler(StoredPlace);

        await PlaceTools.UpdatePlace(
            "place-h", lat: JsonSerializer.Deserialize<FlexibleString>("59.7144"), client: CreateClient(handler));

        Assert.Equal("59.7144", handler.Body!.Value.GetProperty("lat").GetString());
        Assert.Equal("30.4167", handler.Body!.Value.GetProperty("long").GetString());
    }

    [Fact]
    public async Task UpdatePlace_Empty_Coordinate_Removes_It()
    {
        var handler = new PlaceHandler(StoredPlace);

        await PlaceTools.UpdatePlace("place-h", lon: new FlexibleString { Value = "" }, client: CreateClient(handler));

        Assert.Equal("59.7225", handler.Body!.Value.GetProperty("lat").GetString());
        Assert.Equal("", handler.Body!.Value.GetProperty("long").GetString());
    }

    [Fact]
    public async Task CreatePlace_Result_Shows_The_Name_Not_The_Type()
    {
        var handler = new PlaceHandler();

        var result = await PlaceTools.CreatePlace(" Warsaw ", placeType: "City", client: CreateClient(handler));

        Assert.Contains("name: \"Warsaw\"", result);
        Assert.DoesNotContain("name: \"City\"", result);
    }

    [Fact]
    public async Task CreatePlace_Result_Shows_The_Stored_Type_Coordinates_And_Enclosing_Places()
    {
        var handler = new PlaceHandler();

        var result = await PlaceTools.CreatePlace(
            "Москва",
            placeType: "city",
            lat: JsonSerializer.Deserialize<FlexibleString>("55.7558"),
            lon: new FlexibleString { Value = "37.6173" },
            enclosedBy: JsonSerializer.Deserialize<FlexiblePlaceRefList>("""["region-handle-0001"]"""),
            client: CreateClient(handler));

        Assert.Contains("""
            name: "Москва"
            placeType: "City"
            lat: "55.7558"
            lon: "37.6173"
            enclosedBy: ["region-handle-0001"]
            ---
            """.ReplaceLineEndings(), result.ReplaceLineEndings());
    }

    [Fact]
    public async Task CreatePlace_Result_Shows_What_A_Dropped_Argument_Left_Unset()
    {
        // A client drops type and latitude, which the tool does not have, so only the name arrives.
        var handler = new PlaceHandler();

        var result = await PlaceTools.CreatePlace("Kraków", client: CreateClient(handler));

        Assert.Contains("""
            placeType: "Unknown"
            lat: none
            lon: none
            enclosedBy: none
            """.ReplaceLineEndings(), result.ReplaceLineEndings());
    }

    [Fact]
    public async Task UpdatePlace_Result_Shows_The_Stored_Name_Type_Coordinates_And_Enclosing_Places()
    {
        // A client drops type and latitude, so only the name changes and the rest shows as stored.
        var handler = new PlaceHandler(StoredPlace.Replace(
            "\"long\": \"30.4167\"",
            "\"long\": \"30.4167\", \"placeref_list\": [{ \"ref\": \"region-handle-0001\", \"date\": null }]"));

        var result = await PlaceTools.UpdatePlace("place-h", name: "Москва", client: CreateClient(handler));

        Assert.Contains("""
            action: updated
            handle: place-h
            gramps_id: P0007
            name: "Москва"
            placeType: "City"
            lat: "59.7225"
            lon: "30.4167"
            enclosedBy: ["region-handle-0001"]
            ---
            """.ReplaceLineEndings(), result.ReplaceLineEndings());
    }

    [Fact]
    public async Task UpdatePlace_Result_Shows_Removed_Coordinates_As_None()
    {
        var handler = new PlaceHandler(StoredPlace);

        var result = await PlaceTools.UpdatePlace(
            "place-h", lat: new FlexibleString { Value = "" }, lon: new FlexibleString { Value = "" }, client: CreateClient(handler));

        Assert.Contains("lat: none", result);
        Assert.Contains("lon: none", result);
        Assert.Contains("enclosedBy: none", result);
    }

    [Fact]
    public async Task CreatePlace_Stores_The_Type_In_Its_Known_Spelling()
    {
        var handler = new PlaceHandler();

        await PlaceTools.CreatePlace("Warsaw", placeType: "city", client: CreateClient(handler));

        Assert.Equal("City", handler.Body!.Value.GetProperty("place_type").GetString());
    }

    [Fact]
    public async Task UpdatePlace_Stores_The_Type_In_Its_Known_Spelling()
    {
        var handler = new PlaceHandler(StoredPlace);

        await PlaceTools.UpdatePlace("place-h", placeType: "VILLAGE", client: CreateClient(handler));

        Assert.Equal("Village", handler.Body!.Value.GetProperty("place_type").GetString());
    }

    [Fact]
    public async Task UpdatePlace_Rejects_An_Unknown_Type_Before_Saving()
    {
        var handler = new PlaceHandler(StoredPlace);

        var error = await Assert.ThrowsAsync<McpException>(() =>
            PlaceTools.UpdatePlace("place-h", placeType: "Vilage", client: CreateClient(handler)));

        Assert.Contains("Did you mean: Village?", error.Message);
        Assert.Null(handler.Body);
    }

    private static void AssertDate(JsonElement date, int modifier, int day, int month, int year)
    {
        Assert.Equal(modifier, date.GetProperty("modifier").GetInt32());
        var dateval = date.GetProperty("dateval");
        Assert.Equal(day, dateval[0].GetInt32());
        Assert.Equal(month, dateval[1].GetInt32());
        Assert.Equal(year, dateval[2].GetInt32());
    }

    private static GrampsApiClient CreateClient(HttpMessageHandler handler)
    {
        var config = new GrampsConfig("https://place-tools.test", "user", "pass", "tree");
        var http = new HttpClient(handler);
        var tokens = new GrampsAuthTokenProvider(http, config, NullLogger<GrampsAuthTokenProvider>.Instance);
        return new GrampsApiClient(http, config, NullLogger<GrampsApiClient>.Instance, tokens);
    }

    /// <summary>Answers the token and type routes, one stored place, and records the mutation body.</summary>
    private sealed class PlaceHandler(string? storedPlace = null) : HttpMessageHandler
    {
        public JsonElement? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string json;
            if (request.RequestUri!.AbsolutePath.StartsWith("/api/token/"))
                json = """{"access_token":"token","refresh_token":"refresh","expires_in":900}""";
            else if (request.RequestUri.AbsolutePath == "/api/types/default/")
                json = """{"place_types":["City","Village"]}""";
            else if (request.RequestUri.AbsolutePath.StartsWith("/api/types/"))
                json = "{}";
            else if (request.Method == HttpMethod.Get && storedPlace != null)
                json = storedPlace;
            else if (request.Method == HttpMethod.Post || request.Method == HttpMethod.Put)
            {
                Body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken)).RootElement.Clone();
                json = """[{"_class":"Place","type":"add","old":null,"new":{"_class":"Place","handle":"place-h","gramps_id":"P0007"}}]""";
            }
            else
                return new HttpResponseMessage(HttpStatusCode.NotFound);

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }
}
