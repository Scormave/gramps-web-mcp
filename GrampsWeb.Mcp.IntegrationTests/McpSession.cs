using System.Text.Json;
using System.Text.RegularExpressions;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Config;
using GrampsWeb.Mcp.Hosting;
using GrampsWeb.Mcp.Prompts;
using GrampsWeb.Mcp.Resources;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace GrampsWeb.Mcp.IntegrationTests;

/// <summary>
/// The MCP server, hosted in-process the way <c>Program.RunHttpAsync</c> hosts it, with an MCP SDK
/// client connected over Streamable HTTP. Tool calls go through the real protocol pipeline and the
/// server talks to the Gramps Web container.
/// </summary>
public sealed class McpSession : IAsyncDisposable
{
    private const string MapPath = "/mcp";
    private const string NewRecordKey = "new";
    private static readonly TimeSpan CallTimeout = TimeSpan.FromMinutes(1);

    private readonly WebApplication _app;

    private McpSession(WebApplication app, HttpClient http, McpClient client, TokenRequestLog tokenRequests)
    {
        _app = app;
        Http = http;
        Client = client;
        TokenRequests = tokenRequests;
    }

    public McpClient Client { get; }

    /// <summary>Plain HTTP to the in-process server, for <c>/health</c>.</summary>
    public HttpClient Http { get; }

    /// <summary>The Gramps Web token routes this server requested.</summary>
    internal TokenRequestLog TokenRequests { get; }

    /// <param name="config">The server configuration.</param>
    /// <param name="tokenHandler">
    /// Optional handler between the server and Gramps Web's token routes, to change what they answer.
    /// </param>
    public static async Task<McpSession> StartAsync(GrampsConfig config, Func<DelegatingHandler>? tokenHandler = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();

        var tokenRequests = new TokenRequestLog();
        builder.Services.AddGrampsMcpCore(config);
        var tokenClient = builder.Services.AddHttpClient(nameof(GrampsAuthTokenProvider))
            .AddHttpMessageHandler(() => new TokenRoutePacer(tokenRequests));
        if (tokenHandler is not null)
            tokenClient.AddHttpMessageHandler(tokenHandler);
        builder.Services
            .AddMcpServer(options => options.ServerInstructions = GrampsServerInstructions.For(config))
            .WithHttpTransport(options => options.Stateless = true)
            .WithGrampsToolProfile(config)
            .WithResources<GrampsResources>()
            .WithPrompts<GrampsPrompts>();

        var app = builder.Build();
        app.MapGrampsMcpEndpoints(
            new McpTransportConfig(McpListenMode.Http, MapPath, Stateless: true, EnableLegacySse: false),
            new McpAuthConfig([], WarnAboutAnonymousAccess: false),
            McpOriginConfig.Create(null, McpListenMode.Http));
        await app.StartAsync();

        var http = app.GetTestClient();
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = new Uri(http.BaseAddress!, MapPath),
                TransportMode = HttpTransportMode.StreamableHttp
            },
            http,
            loggerFactory: null,
            ownsHttpClient: false);
        var client = await McpClient.CreateAsync(transport);

        return new McpSession(app, http, client, tokenRequests);
    }

    /// <summary>Calls a tool that must succeed and returns its text.</summary>
    public async Task<string> CallAsync(string tool, object? arguments = null)
    {
        var result = await CallToolAsync(tool, arguments);
        var text = TextOf(result);
        Assert.False(result.IsError == true, $"{tool} failed: {text}");
        return text;
    }

    /// <summary>Calls a tool that must fail and returns the error text.</summary>
    public async Task<string> CallErrorAsync(string tool, object? arguments = null)
    {
        var result = await CallToolAsync(tool, arguments);
        var text = TextOf(result);
        Assert.True(result.IsError == true, $"{tool} should have failed but returned: {text}");
        return text;
    }

    public async Task<CallToolResult> CallToolAsync(string tool, object? arguments = null)
    {
        using var timeout = new CancellationTokenSource(CallTimeout);
        return await Client.CallToolAsync(tool, ToArguments(arguments), cancellationToken: timeout.Token);
    }

    /// <summary>Creates one record with create_records and reads its handle and Gramps ID from the reply.</summary>
    /// <param name="records">The record list of create_records it belongs in, such as <c>people</c> or <c>tags</c>.</param>
    /// <param name="record">The fields of the record, without a key.</param>
    public async Task<CreatedRecord> CreateAsync(string records, object record)
    {
        var fields = JsonSerializer.SerializeToNode(record)!.AsObject();
        fields["key"] = NewRecordKey;
        var text = await CallAsync("create_records", new Dictionary<string, object> { [records] = new[] { fields } });
        // Tags have no Gramps ID.
        var match = Regex.Match(text, $@"— handle: (\S+)(?: \| gramps_id: (\S+))? \| key: {NewRecordKey}\r?$",
            RegexOptions.Multiline);
        Assert.True(match.Success, $"No new record in:\n{text}");
        return new CreatedRecord(match.Groups[1].Value, match.Groups[2].Value, Header(text, "action"), text);
    }

    /// <summary>Calls upload_media and reads the handle and Gramps ID from its header.</summary>
    public async Task<CreatedRecord> UploadAsync(object arguments)
    {
        var text = await CallAsync("upload_media", arguments);
        return new CreatedRecord(Header(text, "handle"), Header(text, "gramps_id"), Header(text, "action"), text);
    }

    /// <summary>A field of the YAML header that create and update tools start their result with.</summary>
    public static string Header(string text, string field)
    {
        var match = Regex.Match(text, $@"^{Regex.Escape(field)}:[ \t]*(.*?)[ \t]*$", RegexOptions.Multiline);
        Assert.True(match.Success, $"No {field} in:\n{text}");
        return match.Groups[1].Value;
    }

    public static string TextOf(CallToolResult result) =>
        string.Join("\n", result.Content.OfType<TextContentBlock>().Select(block => block.Text));

    private static Dictionary<string, object?>? ToArguments(object? arguments)
    {
        if (arguments is null)
            return null;

        return JsonSerializer.SerializeToElement(arguments)
            .EnumerateObject()
            .ToDictionary(property => property.Name, property => (object?)property.Value.Clone());
    }

    public async ValueTask DisposeAsync()
    {
        await Client.DisposeAsync();
        Http.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}

/// <summary>
/// What create_records or upload_media reported: the new record, or with action <c>existing</c> the media it reused.
/// </summary>
public sealed record CreatedRecord(string Handle, string GrampsId, string Action, string Text);
