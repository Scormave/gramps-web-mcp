using System.Collections.Concurrent;
using System.Net;
using System.Text;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Config;
using GrampsWeb.Mcp.Exceptions;
using GrampsWeb.Mcp.Formatters;
using GrampsWeb.Mcp.Hosting;
using GrampsWeb.Mcp.Resources;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

public class GrampsTypeVocabulariesTests
{
    [Fact]
    public async Task ClientsSharingTheCacheReadVocabulariesOnce()
    {
        using var handler = new Handler();
        var vocabularies = new GrampsTypeVocabularies();
        var first = Client(handler, vocabularies);
        var second = Client(handler, vocabularies);

        // Separate tool calls: no read scope spans them.
        Assert.Equal("Death", await GrampsDefaultTypeLabels.FormatEventTypeAsync(first, "1"));
        Assert.Equal("Feast", await GrampsDefaultTypeLabels.FormatEventTypeAsync(second, "2"));
        Assert.Equal(1, handler.Count("/api/types/default/event_types"));
        Assert.Equal(1, handler.Count("/api/types/custom/"));

        // A client with its own cache does not see another cache's reads.
        await GrampsDefaultTypeLabels.FormatEventTypeAsync(Client(handler), "1");
        Assert.Equal(2, handler.Count("/api/types/default/event_types"));
    }

    [Fact]
    public async Task RegisteredClientsShareOneCache()
    {
        using var handler = new Handler();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddGrampsMcpCore(new GrampsConfig("https://gramps.test", "user", "pass", "tree"));
        services.ConfigureHttpClientDefaults(builder =>
            builder.ConfigurePrimaryHttpMessageHandler(() => handler));
        await using var provider = services.BuildServiceProvider();

        await provider.GetRequiredService<GrampsApiClient>().GetDefaultTypesAsync();
        await provider.GetRequiredService<GrampsApiClient>().GetDefaultTypesAsync();

        Assert.Equal(1, handler.Count("/api/types/default/"));
    }

    [Fact]
    public async Task CustomTypesAreReadAgainAfterTtl()
    {
        using var handler = new Handler();
        var time = new ManualTime();
        var client = Client(handler, new GrampsTypeVocabularies(time));

        await GrampsDefaultTypeLabels.FormatEventTypeAsync(client, "2");
        time.Advance(GrampsTypeVocabularies.CustomTypesTtl - TimeSpan.FromSeconds(1));
        await GrampsDefaultTypeLabels.FormatEventTypeAsync(client, "2");
        Assert.Equal(1, handler.Count("/api/types/custom/"));

        time.Advance(TimeSpan.FromSeconds(1));
        handler.CustomTypes = """{"event_types":["Wake"]}""";
        Assert.Equal("Wake", await GrampsDefaultTypeLabels.FormatEventTypeAsync(client, "2"));
        Assert.Equal(2, handler.Count("/api/types/custom/"));
        Assert.Equal(1, handler.Count("/api/types/default/event_types"));
    }

    [Fact]
    public async Task FailedReadsAreNotKept()
    {
        using var handler = new Handler();
        var client = Client(handler);

        handler.FailNextRead = true;
        await Assert.ThrowsAsync<GrampsApiException>(() => client.GetDefaultTypesAsync());
        await client.GetDefaultTypesAsync();
        await client.GetDefaultTypesAsync();
        Assert.Equal(2, handler.Count("/api/types/default/"));

        handler.FailNextRead = true;
        await Assert.ThrowsAsync<GrampsApiException>(() => client.GetCustomTypesAsync());
        await client.GetCustomTypesAsync();
        await client.GetCustomTypesAsync();
        Assert.Equal(2, handler.Count("/api/types/custom/"));
    }

    [Fact]
    public async Task ValidationReloadsCustomTypesForUnknownValue()
    {
        using var handler = new Handler { CustomTypes = "{}" };
        var client = Client(handler);

        Assert.Null(await TypeCache.ValidateTypeAsync("birth", "event_types", client));
        Assert.Equal(1, handler.Count("/api/types/custom/"));

        // Added in Gramps after the cached read.
        handler.CustomTypes = """{"event_types":["Feast"]}""";
        Assert.Null(await TypeCache.ValidateTypeAsync("Feast", "event_types", client));
        Assert.Equal(2, handler.Count("/api/types/custom/"));

        var error = await TypeCache.ValidateTypeAsync("Fest", "event_types", client);
        Assert.Contains("Did you mean: Feast?", error);
        Assert.Equal(3, handler.Count("/api/types/custom/"));
        Assert.Equal(1, handler.Count("/api/types/default/"));
    }

    [Fact]
    public async Task TypesListingReadsCustomTypesEachTimeAndSkipsThemOnFailure()
    {
        using var handler = new Handler();
        var client = Client(handler);

        Assert.Contains("Feast", await GrampsResources.FetchTypesTextAsync(client));
        handler.FailNextRead = true;
        var withoutCustom = await GrampsResources.FetchTypesTextAsync(client);

        Assert.Contains("Death", withoutCustom);
        Assert.DoesNotContain("Feast", withoutCustom);
        Assert.Equal(1, handler.Count("/api/types/default/"));
        Assert.Equal(2, handler.Count("/api/types/custom/"));
    }

    private static GrampsApiClient Client(HttpMessageHandler handler, GrampsTypeVocabularies? vocabularies = null)
    {
        var config = new GrampsConfig("https://gramps.test", "user", "pass", "tree");
        var http = new HttpClient(handler, disposeHandler: false);
        return new(http, config, NullLogger<GrampsApiClient>.Instance,
            new GrampsAuthTokenProvider(http, config, NullLogger<GrampsAuthTokenProvider>.Instance),
            typeVocabularies: vocabularies);
    }

    private sealed class ManualTime : TimeProvider
    {
        private long _ticks;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => _ticks;

        public void Advance(TimeSpan by) => _ticks += by.Ticks;
    }

    private sealed class Handler : HttpMessageHandler
    {
        private readonly ConcurrentDictionary<string, int> _paths = new();

        public bool FailNextRead { get; set; }
        public string CustomTypes { get; set; } = """{"event_types":["Feast"]}""";

        public int Count(string path) => _paths.GetValueOrDefault(path);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.PathAndQuery;
            if (path.StartsWith("/api/token/"))
                return Task.FromResult(Response("""{"access_token":"token","refresh_token":"refresh","expires_in":900}"""));
            _paths.AddOrUpdate(path, 1, (_, n) => n + 1);
            if (FailNextRead)
            {
                FailNextRead = false;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                {
                    Content = new StringContent("retry")
                });
            }

            return Task.FromResult(Response(path switch
            {
                "/api/types/default/" => """{"event_types":["Birth","Death"],"place_types":["City"]}""",
                "/api/types/default/event_types" => """["Birth","Death"]""",
                "/api/types/custom/" => CustomTypes,
                _ => "[]"
            }));
        }

        private static HttpResponseMessage Response(string body) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
    }
}
