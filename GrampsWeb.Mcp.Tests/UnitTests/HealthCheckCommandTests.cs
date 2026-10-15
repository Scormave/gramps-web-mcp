using System.Net;
using GrampsWeb.Mcp.Hosting;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

public class HealthCheckCommandTests
{
    [Theory]
    [InlineData(new[] { "--health-check" }, true)]
    [InlineData(new[] { "--health-check", "http://localhost:8080" }, true)]
    [InlineData(new string[0], false)]
    [InlineData(new[] { "--urls", "http://localhost:8080" }, false)]
    [InlineData(new[] { "--urls", "http://localhost:8080", "--health-check" }, false)]
    public void IsRequested_Only_When_First_Argument(string[] args, bool expected)
    {
        Assert.Equal(expected, HealthCheckCommand.IsRequested(args));
    }

    [Theory]
    [InlineData("http://0.0.0.0:8080", "http://localhost:8080/health")]
    [InlineData("http://+:8080", "http://localhost:8080/health")]
    [InlineData("http://*:9000", "http://localhost:9000/health")]
    [InlineData("http://[::]:8080", "http://localhost:8080/health")]
    [InlineData("http://mcp.example.com:8080", "http://localhost:8080/health")]
    [InlineData("http://localhost", "http://localhost/health")]
    [InlineData("http://127.0.0.1:8080", "http://127.0.0.1:8080/health")]
    [InlineData("http://[::1]:8080", "http://[::1]:8080/health")]
    [InlineData("https://0.0.0.0:8443;http://0.0.0.0:8080", "http://localhost:8080/health")]
    [InlineData("https://0.0.0.0:8443", "https://localhost:8443/health")]
    [InlineData(" ; http://0.0.0.0:8080 ", "http://localhost:8080/health")]
    [InlineData("http://unix:/tmp/mcp.sock;http://0.0.0.0:8080", "http://localhost:8080/health")]
    public void ResolveUrl_Uses_Aspnetcore_Urls(string urls, string expected)
    {
        var url = HealthCheckCommand.ResolveUrl(Environment(("ASPNETCORE_URLS", urls)));

        Assert.Equal(expected, url.ToString());
    }

    [Fact]
    public void ResolveUrl_Prefers_Aspnetcore_Urls_Over_Dotnet_Urls_And_Ports()
    {
        var url = HealthCheckCommand.ResolveUrl(Environment(
            ("ASPNETCORE_URLS", "http://0.0.0.0:8080"),
            ("DOTNET_URLS", "http://0.0.0.0:7000"),
            ("ASPNETCORE_HTTP_PORTS", "6000")));

        Assert.Equal("http://localhost:8080/health", url.ToString());
    }

    [Fact]
    public void ResolveUrl_Falls_Back_To_Dotnet_Urls()
    {
        var url = HealthCheckCommand.ResolveUrl(Environment(("DOTNET_URLS", "http://0.0.0.0:7000")));

        Assert.Equal("http://localhost:7000/health", url.ToString());
    }

    [Theory]
    [InlineData("ASPNETCORE_HTTP_PORTS", "8080", "http://localhost:8080/health")]
    [InlineData("ASPNETCORE_HTTP_PORTS", "8081;8082", "http://localhost:8081/health")]
    [InlineData("DOTNET_HTTP_PORTS", "7000", "http://localhost:7000/health")]
    [InlineData("ASPNETCORE_HTTP_PORTS", "not-a-port", "http://localhost:5000/health")]
    public void ResolveUrl_Uses_Http_Ports_Without_Urls(string name, string value, string expected)
    {
        var url = HealthCheckCommand.ResolveUrl(Environment((name, value)));

        Assert.Equal(expected, url.ToString());
    }

    [Fact]
    public void ResolveUrl_Defaults_To_Kestrel_Default()
    {
        var url = HealthCheckCommand.ResolveUrl(Environment());

        Assert.Equal("http://localhost:5000/health", url.ToString());
    }

    [Theory]
    [InlineData("http://localhost:8080", "http://localhost:8080/health")]
    [InlineData("http://localhost:8080/", "http://localhost:8080/health")]
    [InlineData("https://mcp.example.com/health", "https://mcp.example.com/health")]
    [InlineData("http://localhost:8080/custom/health", "http://localhost:8080/custom/health")]
    public void TryParseExplicitUrl_Accepts_Http_Urls(string value, string expected)
    {
        Assert.True(HealthCheckCommand.TryParseExplicitUrl(value, out var url));
        Assert.Equal(expected, url.ToString());
    }

    [Theory]
    [InlineData("localhost:8080")]
    [InlineData("/health")]
    [InlineData("ftp://localhost/health")]
    public void TryParseExplicitUrl_Refuses_Other_Values(string value)
    {
        Assert.False(HealthCheckCommand.TryParseExplicitUrl(value, out _));
    }

    [Fact]
    public async Task RunAsync_Returns_0_When_Health_Answers_200()
    {
        var handler = new StubHandler(HttpStatusCode.OK);
        var output = new StringWriter();

        var exitCode = await HealthCheckCommand.RunAsync(
            ["--health-check"], output, Environment(("MCP_TRANSPORT", "http"), ("ASPNETCORE_URLS", "http://0.0.0.0:8080")),
            handler);

        Assert.Equal(0, exitCode);
        Assert.Equal("http://localhost:8080/health", handler.RequestedUrl?.ToString());
        Assert.Equal("healthy: http://localhost:8080/health answered 200 OK", output.ToString().Trim());
    }

    [Fact]
    public async Task RunAsync_Returns_1_When_Health_Answers_503()
    {
        var output = new StringWriter();

        var exitCode = await HealthCheckCommand.RunAsync(
            ["--health-check"], output, Environment(("MCP_TRANSPORT", "http"), ("ASPNETCORE_URLS", "http://0.0.0.0:8080")),
            new StubHandler(HttpStatusCode.ServiceUnavailable));

        Assert.Equal(1, exitCode);
        Assert.Equal(
            "unhealthy: http://localhost:8080/health answered 503 Service Unavailable",
            output.ToString().Trim());
    }

    [Fact]
    public async Task RunAsync_Returns_1_When_Server_Does_Not_Answer()
    {
        var output = new StringWriter();

        var exitCode = await HealthCheckCommand.RunAsync(
            ["--health-check"], output, Environment(("MCP_TRANSPORT", "http")),
            new StubHandler(new HttpRequestException("Connection refused (localhost:5000)")));

        Assert.Equal(1, exitCode);
        Assert.Equal(
            "unhealthy: http://localhost:5000/health did not answer: Connection refused (localhost:5000)",
            output.ToString().Trim());
    }

    [Fact]
    public async Task RunAsync_Returns_1_When_Request_Times_Out()
    {
        var output = new StringWriter();

        var exitCode = await HealthCheckCommand.RunAsync(
            ["--health-check"], output, Environment(("MCP_TRANSPORT", "sse")),
            new StubHandler(new TaskCanceledException("timeout", new TimeoutException())));

        Assert.Equal(1, exitCode);
        Assert.Equal("unhealthy: http://localhost:5000/health did not answer within 10 s", output.ToString().Trim());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("stdio")]
    public async Task RunAsync_Skips_Stdio_Without_Request(string? transport)
    {
        var handler = new StubHandler(HttpStatusCode.ServiceUnavailable);
        var output = new StringWriter();

        var exitCode = await HealthCheckCommand.RunAsync(
            ["--health-check"], output, Environment(("MCP_TRANSPORT", transport)), handler);

        Assert.Equal(0, exitCode);
        Assert.Null(handler.RequestedUrl);
        Assert.StartsWith("skipped:", output.ToString());
    }

    [Fact]
    public async Task RunAsync_Explicit_Url_Checks_Even_In_Stdio_Mode()
    {
        var handler = new StubHandler(HttpStatusCode.OK);
        var output = new StringWriter();

        var exitCode = await HealthCheckCommand.RunAsync(
            ["--health-check", "http://127.0.0.1:9000"], output, Environment(), handler);

        Assert.Equal(0, exitCode);
        Assert.Equal("http://127.0.0.1:9000/health", handler.RequestedUrl?.ToString());
    }

    [Theory]
    [InlineData("--health-check", "not a url")]
    [InlineData("--health-check", "http://localhost:8080", "extra")]
    public async Task RunAsync_Returns_1_On_Bad_Arguments(params string[] args)
    {
        var handler = new StubHandler(HttpStatusCode.OK);
        var output = new StringWriter();

        var exitCode = await HealthCheckCommand.RunAsync(args, output, Environment(("MCP_TRANSPORT", "http")), handler);

        Assert.Equal(1, exitCode);
        Assert.Null(handler.RequestedUrl);
    }

    private static Func<string, string?> Environment(params (string Name, string? Value)[] variables)
    {
        var values = variables.ToDictionary(v => v.Name, v => v.Value);
        return name => values.GetValueOrDefault(name);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly Exception? _exception;

        public StubHandler(HttpStatusCode status) => _status = status;

        public StubHandler(Exception exception) => _exception = exception;

        public Uri? RequestedUrl { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestedUrl = request.RequestUri;
            if (_exception is not null)
                throw _exception;

            return Task.FromResult(new HttpResponseMessage(_status));
        }
    }
}
