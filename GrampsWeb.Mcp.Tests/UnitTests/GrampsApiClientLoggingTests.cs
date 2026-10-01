using System.Net;
using System.Text;
using System.Text.Json;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Config;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

public class GrampsApiClientLoggingTests
{
    private const string PersonBody = """[{"handle":"h1","first_name":"Ivanov","birth":"25 Oct 2019"}]""";

    [Fact]
    public async Task Information_Logs_Omit_Bodies_And_Query_Strings()
    {
        var logger = new CapturingLogger(LogLevel.Information);
        var client = CreateClient(logger);

        await client.GetAsync<JsonElement>("/api/people/?gql=first_name%3DIvanov&page=1");

        Assert.NotEmpty(logger.Messages);
        Assert.Contains(logger.Messages, m => m.Contains("GET /api/people/ Status=200"));
        Assert.Contains(logger.Messages, m => m.Contains($"BodyLength={PersonBody.Length}"));
        Assert.DoesNotContain(logger.Messages, m => m.Contains("Ivanov"));
        Assert.DoesNotContain(logger.Messages, m => m.Contains("gql"));
        Assert.DoesNotContain(logger.Messages, m => m.Contains("2019"));
    }

    [Fact]
    public async Task Debug_Logs_Include_Sanitized_Bodies()
    {
        var logger = new CapturingLogger(LogLevel.Debug);
        var client = CreateClient(logger);

        await client.GetAsync<JsonElement>("/api/people/?page=1");

        Assert.Contains(logger.Messages, m => m.Contains("Body=") && m.Contains("Ivanov"));
        Assert.Contains(logger.Messages, m => m.Contains("/api/people/?page=1"));
    }

    private static GrampsApiClient CreateClient(ILogger<GrampsApiClient> logger)
    {
        var http = new HttpClient(new PeopleHandler());
        var config = new GrampsConfig("https://gramps-web.test", "user", "pass", "tree");
        var tokens = new GrampsAuthTokenProvider(http, config, NullLogger<GrampsAuthTokenProvider>.Instance);
        return new GrampsApiClient(http, config, logger, tokens);
    }

    private sealed class PeopleHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var json = request.RequestUri!.AbsolutePath == "/api/token/"
                ? """{"access_token":"token","refresh_token":"refresh","expires_in":900}"""
                : PersonBody;

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class CapturingLogger(LogLevel minLevel) : ILogger<GrampsApiClient>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= minLevel;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
                Messages.Add(formatter(state, exception));
        }
    }
}
