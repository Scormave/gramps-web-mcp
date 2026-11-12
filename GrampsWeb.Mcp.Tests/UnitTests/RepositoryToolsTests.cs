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

public class RepositoryToolsTests
{
    private const string StoredRepository = """
        {
          "_class": "Repository", "handle": "h-repo", "gramps_id": "R0100", "name": "City Archive", "type": "Archive",
          "address_list": [{ "_class": "Address", "street": "Main St 1", "city": "Springfield", "private": false,
                             "date": { "_class": "Date", "calendar": 0, "modifier": 0, "quality": 0,
                                       "dateval": [0, 0, 0, false], "text": "", "sortval": 0, "newyear": 0 },
                             "citation_list": [], "note_list": [] }],
          "urls": [{ "_class": "Url", "path": "https://archive.example.org", "desc": "", "type": "Web Home", "private": false }],
          "note_list": [], "tag_list": [], "private": false
        }
        """;

    private const string KeyedAddress = "street: 12 High Street\ncity: London\npostal: SW1A 1AA\ncountry: United Kingdom";

    private const string TwoLinks = "Web Home: https://archive.example.org — reading room\nE-mail: info@archive.example.org";

    [Fact]
    public async Task CreateRecords_Result_Names_The_Repository_Not_Its_Type()
    {
        var result = await CreateRepository("""{ "name": "City Archive", "repoType": "Library" }""", new RepositoryHandler());

        Assert.Contains("Repository: City Archive — handle:", result);
        Assert.Contains("gramps_id: R0001", result);
        Assert.DoesNotContain("Library", result);
    }

    [Fact]
    public async Task CreateRecords_Addresses_Fill_The_Address_Fields()
    {
        var handler = new RepositoryHandler();

        await CreateRepository($$"""{ "name": "City Archive", "addresses": {{JsonSerializer.Serialize(KeyedAddress)}} }""", handler);

        var address = Assert.Single(handler.Body!["address_list"]!.AsArray())!;
        Assert.Equal("12 High Street", address["street"]!.GetValue<string>());
        Assert.Equal("London", address["city"]!.GetValue<string>());
        Assert.Equal("SW1A 1AA", address["postal"]!.GetValue<string>());
        Assert.Equal("United Kingdom", address["country"]!.GetValue<string>());
    }

    [Fact]
    public async Task CreateRecords_Address_Line_Is_Stored_As_The_Street()
    {
        var handler = new RepositoryHandler();

        await CreateRepository("""{ "name": "City Archive", "address": "12 High Street, London" }""", handler);

        var address = Assert.Single(handler.Body!["address_list"]!.AsArray())!;
        Assert.Equal("12 High Street, London", address["street"]!.GetValue<string>());
        Assert.Null(address["city"]);
    }

    [Fact]
    public async Task CreateRecords_Refuses_Address_And_Addresses_Together()
    {
        var handler = new RepositoryHandler();

        var error = await Assert.ThrowsAsync<McpException>(() => CreateRepository($$"""
            { "name": "City Archive", "address": "12 High Street", "addresses": {{JsonSerializer.Serialize(KeyedAddress)}} }
            """, handler));

        Assert.Contains("address or addresses, not both", error.Message);
        Assert.Null(handler.Body);
    }

    [Fact]
    public async Task UpdateRepository_Addresses_Replace_The_Stored_Ones_And_Keep_The_Rest()
    {
        var handler = new RepositoryHandler(StoredRepository);

        await RepositoryTools.UpdateRepository("h-repo", addresses: Addresses(KeyedAddress), client: CreateClient(handler));

        var expected = JsonNode.Parse(StoredRepository)!.AsObject();
        expected.Remove("address_list");
        var sent = handler.Body!.AsObject();
        var address = Assert.Single(sent["address_list"]!.AsArray())!;
        Assert.Equal("12 High Street", address["street"]!.GetValue<string>());
        Assert.Equal("London", address["city"]!.GetValue<string>());
        sent.Remove("address_list");
        Assert.True(JsonNode.DeepEquals(expected, sent), $"Expected:\n{expected}\nActual:\n{sent}");
    }

    [Fact]
    public async Task UpdateRepository_Empty_Addresses_Remove_The_Stored_Ones()
    {
        var handler = new RepositoryHandler(StoredRepository);

        await RepositoryTools.UpdateRepository("h-repo", addresses: Addresses("[]"), client: CreateClient(handler));

        Assert.Empty(handler.Body!["address_list"]!.AsArray());
    }

    [Fact]
    public async Task UpdateRepository_Refuses_Address_And_Addresses_Together()
    {
        var handler = new RepositoryHandler(StoredRepository);

        await Assert.ThrowsAsync<McpException>(() => RepositoryTools.UpdateRepository("h-repo", address: "",
            addresses: Addresses("[]"), client: CreateClient(handler)));

        Assert.Null(handler.Body);
    }

    [Fact]
    public async Task CreateRecords_Urls_Keep_Each_Type_And_Description()
    {
        var handler = new RepositoryHandler();

        await CreateRepository($$"""{ "name": "City Archive", "urls": {{JsonSerializer.Serialize(TwoLinks)}} }""", handler);

        var urls = handler.Body!["urls"]!.AsArray();
        Assert.Equal(2, urls.Count);
        Assert.Equal("Web Home", urls[0]!["type"]!.GetValue<string>());
        Assert.Equal("https://archive.example.org", urls[0]!["path"]!.GetValue<string>());
        Assert.Equal("reading room", urls[0]!["desc"]!.GetValue<string>());
        Assert.Equal("E-mail", urls[1]!["type"]!.GetValue<string>());
        Assert.Equal("info@archive.example.org", urls[1]!["path"]!.GetValue<string>());
    }

    [Fact]
    public async Task CreateRecords_Url_Is_Stored_As_A_Web_Home_Link()
    {
        var handler = new RepositoryHandler();

        await CreateRepository("""{ "name": "City Archive", "url": "https://archive.example.org" }""", handler);

        var url = Assert.Single(handler.Body!["urls"]!.AsArray())!;
        Assert.Equal("Web Home", url["type"]!.GetValue<string>());
        Assert.Equal("https://archive.example.org", url["path"]!.GetValue<string>());
    }

    [Fact]
    public async Task CreateRecords_Refuses_Url_And_Urls_Together()
    {
        var handler = new RepositoryHandler();

        var error = await Assert.ThrowsAsync<McpException>(() => CreateRepository($$"""
            { "name": "City Archive", "url": "https://archive.example.org", "urls": {{JsonSerializer.Serialize(TwoLinks)}} }
            """, handler));

        Assert.Contains("url or urls, not both", error.Message);
        Assert.Null(handler.Body);
    }

    [Fact]
    public async Task CreateRecords_Rejects_An_Unknown_Url_Type_Before_Saving()
    {
        var handler = new RepositoryHandler();

        var error = await Assert.ThrowsAsync<McpException>(() => CreateRepository(
            """{ "name": "City Archive", "urls": "Web Hom: https://archive.example.org" }""", handler));

        Assert.Contains("Did you mean: Web Home", error.Message);
        Assert.Null(handler.Body);
    }

    [Fact]
    public async Task UpdateRepository_Urls_Replace_The_Stored_Ones_And_Keep_The_Rest()
    {
        var handler = new RepositoryHandler(StoredRepository);

        await RepositoryTools.UpdateRepository("h-repo", urls: Urls(TwoLinks), client: CreateClient(handler));

        var expected = JsonNode.Parse(StoredRepository)!.AsObject();
        expected.Remove("urls");
        var sent = handler.Body!.AsObject();
        var urls = sent["urls"]!.AsArray();
        Assert.Equal(["Web Home", "E-mail"], urls.Select(u => u!["type"]!.GetValue<string>()));
        Assert.Equal("reading room", urls[0]!["desc"]!.GetValue<string>());
        sent.Remove("urls");
        Assert.True(JsonNode.DeepEquals(expected, sent), $"Expected:\n{expected}\nActual:\n{sent}");
    }

    [Fact]
    public async Task UpdateRepository_Empty_Urls_Remove_The_Stored_Ones()
    {
        var handler = new RepositoryHandler(StoredRepository);

        await RepositoryTools.UpdateRepository("h-repo", urls: Urls("[]"), client: CreateClient(handler));

        Assert.Empty(handler.Body!["urls"]!.AsArray());
    }

    [Fact]
    public async Task UpdateRepository_Refuses_Url_And_Urls_Together()
    {
        var handler = new RepositoryHandler(StoredRepository);

        await Assert.ThrowsAsync<McpException>(() => RepositoryTools.UpdateRepository("h-repo", url: "",
            urls: Urls("[]"), client: CreateClient(handler)));

        Assert.Null(handler.Body);
    }

    [Fact]
    public async Task ReadRepository_Shows_Addresses_And_Urls_As_Text()
    {
        var handler = new RepositoryHandler(StoredRepository);

        var result = await RepositoryTools.ReadRepositoryAsync("h-repo", CreateClient(handler));

        Assert.Contains("• Main St 1, Springfield", result);
        Assert.Contains("• [Web Home] https://archive.example.org", result);
        Assert.DoesNotContain("_class", result);
        Assert.DoesNotContain("resident as of", result);
    }

    /// <summary>An addresses argument as the MCP client sends it: a JSON string or array.</summary>
    private static FlexibleAddressList Addresses(string value) =>
        JsonSerializer.Deserialize<FlexibleAddressList>(value.TrimStart().StartsWith('[') ? value : JsonSerializer.Serialize(value))!;

    /// <summary>A urls argument as the MCP client sends it: a JSON string or array.</summary>
    private static FlexibleUrlList Urls(string value) =>
        JsonSerializer.Deserialize<FlexibleUrlList>(value.TrimStart().StartsWith('[') ? value : JsonSerializer.Serialize(value))!;

    /// <summary>Creates <paramref name="repository"/> through create_records.</summary>
    private static Task<string> CreateRepository(string repository, RepositoryHandler handler) =>
        CreateRecordsCall.RunAsync($$"""{ "repositories": [{{repository}}] }""", CreateClient(handler));

    private static GrampsApiClient CreateClient(RepositoryHandler handler)
    {
        var config = new GrampsConfig("https://repository-tools.test", "user", "pass", "tree");
        var http = new HttpClient(handler);
        var tokens = new GrampsAuthTokenProvider(http, config, NullLogger<GrampsAuthTokenProvider>.Instance);
        return new GrampsApiClient(http, config, NullLogger<GrampsApiClient>.Instance, tokens);
    }

    /// <summary>
    /// Answers the token and type routes, a create as Gramps Web does and a repository put (recording the saved
    /// repository), and every GET of a repository with <paramref name="stored"/>.
    /// </summary>
    private sealed class RepositoryHandler(string? stored = null) : HttpMessageHandler
    {
        public JsonNode? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            JsonNode? posted = null;
            if (request.Method == HttpMethod.Post && path == "/api/objects/")
            {
                posted = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!;
                Body = CreateRecordsCall.Saved(posted, "Repository");
            }
            else if (request.Method == HttpMethod.Put)
                Body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));

            var json = path switch
            {
                "/api/token/" => """{"access_token":"token","refresh_token":"refresh","expires_in":900}""",
                "/api/types/default/" =>
                    """{"repository_types":["Archive","Library"],"url_types":["Unknown","E-mail","Web Home","Web Search","FTP"]}""",
                "/api/objects/" when posted != null => CreateRecordsCall.Changes(posted).ToJsonString(),
                _ when path.StartsWith("/api/repositories/") && request.Method == HttpMethod.Put => "{}",
                _ when path.StartsWith("/api/repositories/") && stored != null => stored,
                _ when path.StartsWith("/api/types/") => "{}",
                _ => null
            };

            return json == null
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }
}
