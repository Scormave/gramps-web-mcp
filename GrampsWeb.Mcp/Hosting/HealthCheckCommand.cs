using System.Net;
using GrampsWeb.Mcp.Config;
using Microsoft.AspNetCore.Http;

namespace GrampsWeb.Mcp.Hosting;

/// <summary>
/// <c>--health-check [url]</c>: asks a running server's <c>GET /health</c> and exits with 0 when it answers with a
/// success status and 1 otherwise, printing one line about the result. The Docker image's <c>HEALTHCHECK</c> runs
/// it, so the image needs no curl. Without a URL it finds the server's address the way Kestrel does, from
/// <c>ASPNETCORE_URLS</c>, <c>DOTNET_URLS</c>, <c>ASPNETCORE_HTTP_PORTS</c> or <c>DOTNET_HTTP_PORTS</c>.
/// </summary>
internal static class HealthCheckCommand
{
    public const string Argument = "--health-check";

    private const string HealthPath = "/health";

    /// <summary>Kestrel's address when no URL or port is configured.</summary>
    private const string KestrelDefaultUrl = "http://localhost:5000";

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

    public static bool IsRequested(IReadOnlyList<string> args) => args.Count > 0 && args[0] == Argument;

    /// <returns>The process exit code: 0 when healthy, 1 otherwise.</returns>
    public static async Task<int> RunAsync(
        IReadOnlyList<string> args,
        TextWriter output,
        Func<string, string?>? environment = null,
        HttpMessageHandler? handler = null)
    {
        environment ??= Environment.GetEnvironmentVariable;

        Uri url;
        if (args.Count > 2)
        {
            await output.WriteLineAsync($"usage: {Argument} [url]");
            return 1;
        }

        if (args.Count == 2)
        {
            if (!TryParseExplicitUrl(args[1], out url))
            {
                await output.WriteLineAsync($"unhealthy: {args[1]} is not an http or https URL");
                return 1;
            }
        }
        else if (McpTransportConfig.ParseMode(environment("MCP_TRANSPORT")) == McpListenMode.Stdio)
        {
            // A stdio server has no HTTP endpoint; reporting it unhealthy would only get the container restarted.
            await output.WriteLineAsync("skipped: MCP_TRANSPORT is stdio, there is no HTTP endpoint to check");
            return 0;
        }
        else
        {
            url = ResolveUrl(environment);
        }

        using var client = new HttpClient(handler ?? new SocketsHttpHandler { UseProxy = false })
        {
            Timeout = RequestTimeout
        };

        try
        {
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            var status = $"{(int)response.StatusCode} {response.ReasonPhrase}".TrimEnd();
            if (response.IsSuccessStatusCode)
            {
                await output.WriteLineAsync($"healthy: {url} answered {status}");
                return 0;
            }

            await output.WriteLineAsync($"unhealthy: {url} answered {status}");
            return 1;
        }
        catch (HttpRequestException ex)
        {
            await output.WriteLineAsync($"unhealthy: {url} did not answer: {ex.Message}");
            return 1;
        }
        catch (TaskCanceledException)
        {
            await output.WriteLineAsync($"unhealthy: {url} did not answer within {RequestTimeout.TotalSeconds:0} s");
            return 1;
        }
    }

    /// <summary>
    /// The health URL of the server configured by the environment: the first http address of
    /// <c>ASPNETCORE_URLS</c> (or <c>DOTNET_URLS</c>), else the first https one, else the first port of
    /// <c>ASPNETCORE_HTTP_PORTS</c> (or <c>DOTNET_HTTP_PORTS</c>), else Kestrel's default. A host that Kestrel
    /// treats as "all addresses" (<c>0.0.0.0</c>, <c>[::]</c>, <c>*</c>, <c>+</c> or any name) becomes
    /// <c>localhost</c>.
    /// </summary>
    internal static Uri ResolveUrl(Func<string, string?> environment)
    {
        var urls = FirstSet(environment, "ASPNETCORE_URLS", "DOTNET_URLS");
        if (urls is not null)
        {
            var addresses = urls
                .Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Select(TryParseBindingAddress)
                .OfType<BindingAddress>()
                .Where(address => !address.IsUnixPipe && !address.IsNamedPipe)
                .ToList();
            var address = addresses.FirstOrDefault(a => a.Scheme == Uri.UriSchemeHttp) ?? addresses.FirstOrDefault();
            if (address is not null)
            {
                return HealthUrl(address.Scheme, ProbeHost(address.Host), address.Port);
            }
        }

        var ports = FirstSet(environment, "ASPNETCORE_HTTP_PORTS", "DOTNET_HTTP_PORTS");
        foreach (var port in ports?.Split(';', StringSplitOptions.TrimEntries) ?? [])
        {
            if (int.TryParse(port, out var number) && number is > 0 and <= 65535)
            {
                return HealthUrl(Uri.UriSchemeHttp, "localhost", number);
            }
        }

        return new Uri(new Uri(KestrelDefaultUrl), HealthPath);
    }

    /// <summary>
    /// Accepts an absolute http or https URL. A URL without a path, such as <c>http://localhost:8080</c>, means
    /// that server's <c>/health</c>.
    /// </summary>
    internal static bool TryParseExplicitUrl(string value, out Uri url)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var parsed)
            || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
        {
            url = null!;
            return false;
        }

        url = parsed.AbsolutePath == "/" && parsed.Query.Length == 0 ? new Uri(parsed, HealthPath) : parsed;
        return true;
    }

    private static string? FirstSet(Func<string, string?> environment, params string[] names) =>
        names.Select(environment).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static BindingAddress? TryParseBindingAddress(string value)
    {
        try
        {
            return BindingAddress.Parse(value);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>
    /// Kestrel listens on every address for a wildcard or for any host name but <c>localhost</c>; reach those
    /// through loopback. A specific IP address is probed as written.
    /// </summary>
    private static string ProbeHost(string host)
    {
        var bare = host.StartsWith('[') && host.EndsWith(']') ? host[1..^1] : host;
        return IPAddress.TryParse(bare, out var ip) && !ip.Equals(IPAddress.Any) && !ip.Equals(IPAddress.IPv6Any)
            ? host
            : "localhost";
    }

    private static Uri HealthUrl(string scheme, string host, int port) =>
        new UriBuilder(scheme, host, port, HealthPath).Uri;
}
