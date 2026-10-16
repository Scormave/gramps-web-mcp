using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Config;
using GrampsWeb.Mcp.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

public class TagToolsTests
{
    private const string StoredTags = """
        [
          { "_class": "Tag", "handle": "t-todo", "name": "ToDo", "color": "#1f77b4", "priority": 0,
            "change": 1700000000, "future_field": { "kept": true } },
          { "_class": "Tag", "handle": "t-book", "name": "Метрическая книга", "color": "#45b41f", "priority": 1,
            "change": 1700000001 }
        ]
        """;

    [Fact]
    public async Task UpdateTag_Changes_Only_What_It_Was_Given_And_Reports_The_Stored_Values()
    {
        var handler = new TagHandler();

        var result = await TagTools.UpdateTag("t-todo", name: "Проверить", color: "FF5733", client: CreateClient(handler));

        var expected = handler.Stored("t-todo");
        expected["name"] = "Проверить";
        expected["color"] = "#ff5733";
        Assert.True(JsonNode.DeepEquals(expected, handler.Body), handler.Body?.ToJsonString());
        Assert.Equal("PUT /api/tags/t-todo", handler.BodyRoute);
        Assert.Contains("action: updated", result);
        Assert.Contains("handle: t-todo", result);
        Assert.Contains("name: \"Проверить\"", result);
        Assert.Contains("color: \"#ff5733\"", result);
        Assert.Contains("priority: 0", result);
        Assert.DoesNotContain("gramps_id", result);
    }

    [Theory]
    [InlineData("Метрическая книга")]
    [InlineData(" метрическая КНИГА ")]
    public async Task UpdateTag_Finds_The_Tag_By_Its_Name(string tag)
    {
        var handler = new TagHandler();

        await TagTools.UpdateTag(tag, priority: 3, client: CreateClient(handler));

        Assert.Equal("PUT /api/tags/t-book", handler.BodyRoute);
        Assert.Equal(3, handler.Body!["priority"]!.GetValue<int>());
        Assert.Equal("Метрическая книга", handler.Body["name"]!.GetValue<string>());
        Assert.Equal("#45b41f", handler.Body["color"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("#45B41F")]
    [InlineData(" 45b41f ")]
    public async Task UpdateTag_Stores_The_Color_With_Hash_In_Lower_Case(string color)
    {
        var handler = new TagHandler();

        await TagTools.UpdateTag("t-todo", color: color, client: CreateClient(handler));

        Assert.Equal("#45b41f", handler.Body!["color"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("red")]
    [InlineData("#12345")]
    [InlineData("1234567")]
    [InlineData("GGGGGG")]
    public async Task UpdateTag_Rejects_A_Color_That_Is_Not_Six_Hex_Digits(string color)
    {
        var handler = new TagHandler();

        var error = await Assert.ThrowsAsync<McpException>(() =>
            TagTools.UpdateTag("t-todo", color: color, client: CreateClient(handler)));

        Assert.StartsWith("Error: color must be six hex digits", error.Message);
        Assert.Null(handler.Body);
    }

    [Fact]
    public async Task UpdateTag_Rejects_A_Name_Another_Tag_Uses()
    {
        var handler = new TagHandler();

        var error = await Assert.ThrowsAsync<McpException>(() =>
            TagTools.UpdateTag("t-todo", name: "метрическая КНИГА", client: CreateClient(handler)));

        Assert.StartsWith("Error: another tag is already named \"Метрическая книга\" (handle t-book)", error.Message);
        Assert.Null(handler.Body);
    }

    [Fact]
    public async Task UpdateTag_Can_Change_The_Case_Of_Its_Own_Name()
    {
        var handler = new TagHandler();

        await TagTools.UpdateTag("ToDo", name: "TODO", client: CreateClient(handler));

        Assert.Equal("TODO", handler.Body!["name"]!.GetValue<string>());
    }

    [Fact]
    public async Task UpdateTag_Rejects_An_Empty_Name()
    {
        var handler = new TagHandler();

        await Assert.ThrowsAsync<McpException>(() => TagTools.UpdateTag("t-todo", name: " ", client: CreateClient(handler)));

        Assert.Null(handler.Body);
    }

    [Fact]
    public async Task UpdateTag_Reports_A_Missing_Tag_Without_Saving()
    {
        var handler = new TagHandler();

        var result = await TagTools.UpdateTag("Нет такой", name: "Новая", client: CreateClient(handler));

        Assert.StartsWith("Tag not found: Нет такой", result);
        Assert.Null(handler.Body);
    }

    [Theory]
    [InlineData(null, "#000000")]
    [InlineData("#AbCdEf", "#abcdef")]
    [InlineData("FF5733", "#ff5733")]
    public async Task CreateTag_Stores_The_Color_With_Hash(string? color, string stored)
    {
        var handler = new TagHandler();
        var client = CreateClient(handler);

        var result = color == null
            ? await TagTools.CreateTag("Новый", client: client)
            : await TagTools.CreateTag("Новый", color, client: client);

        Assert.Equal("POST /api/tags/", handler.BodyRoute);
        Assert.Equal(stored, handler.Body!["color"]!.GetValue<string>());
        Assert.Contains("handle: t-new", result);
    }

    private static GrampsApiClient CreateClient(HttpMessageHandler handler)
    {
        var config = new GrampsConfig("https://tag-tools.test", "user", "pass", "tree");
        var http = new HttpClient(handler);
        var tokens = new GrampsAuthTokenProvider(http, config, NullLogger<GrampsAuthTokenProvider>.Instance);
        return new GrampsApiClient(http, config, NullLogger<GrampsApiClient>.Instance, tokens);
    }

    /// <summary>Serves the stored tags and records the body of the one write a tool sends.</summary>
    private sealed class TagHandler : HttpMessageHandler
    {
        private readonly JsonArray _tags = JsonNode.Parse(StoredTags)!.AsArray();

        public JsonObject? Body { get; private set; }

        public string? BodyRoute { get; private set; }

        public JsonObject Stored(string handle) =>
            _tags.Single(tag => tag!["handle"]!.GetValue<string>() == handle)!.DeepClone().AsObject();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/token/")
                return Json("""{"access_token":"token","refresh_token":"refresh","expires_in":900}""");

            if (request.Method != HttpMethod.Get)
            {
                Body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!.AsObject();
                BodyRoute = $"{request.Method} {path}";
                return request.Method == HttpMethod.Post
                    ? Json("""[{"_class":"Tag","type":"add","old":null,"new":{"_class":"Tag","handle":"t-new"}}]""")
                    : Json("[]");
            }

            if (path == "/api/tags/")
                return Json(_tags.ToJsonString());
            var handle = Uri.UnescapeDataString(path["/api/tags/".Length..]);
            return _tags.Any(tag => tag!["handle"]!.GetValue<string>() == handle)
                ? Json(Stored(handle).ToJsonString())
                : new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Json(string json) =>
            new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }
}
