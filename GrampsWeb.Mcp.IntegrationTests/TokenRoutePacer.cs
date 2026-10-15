namespace GrampsWeb.Mcp.IntegrationTests;

/// <summary>
/// Spaces requests to Gramps Web's token routes, which allow one request per second from an address,
/// across every server and client in the run, and counts the requests of the one it is attached to.
/// The shared session logs in once, so a test that sees 429 from the token route found a client that
/// logs in more often than it should.
/// </summary>
internal sealed class TokenRoutePacer(TokenRequestLog log) : DelegatingHandler
{
    private static readonly TimeSpan Spacing = TimeSpan.FromMilliseconds(1100);
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static DateTime _lastRequest = DateTime.MinValue;

    public TokenRoutePacer(TokenRequestLog log, HttpMessageHandler innerHandler) : this(log)
    {
        InnerHandler = innerHandler;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var path = request.RequestUri?.AbsolutePath ?? "";
        if (!path.StartsWith("/api/token/", StringComparison.Ordinal))
            return await base.SendAsync(request, cancellationToken);

        await Gate.WaitAsync(cancellationToken);
        try
        {
            var wait = _lastRequest + Spacing - DateTime.UtcNow;
            if (wait > TimeSpan.Zero)
                await Task.Delay(wait, cancellationToken);

            log.Add(path);
            return await base.SendAsync(request, cancellationToken);
        }
        finally
        {
            _lastRequest = DateTime.UtcNow;
            Gate.Release();
        }
    }
}

/// <summary>The token routes one server or client requested, in order.</summary>
internal sealed class TokenRequestLog
{
    private readonly List<string> _paths = [];

    public IReadOnlyList<string> Paths
    {
        get
        {
            lock (_paths)
                return _paths.ToArray();
        }
    }

    public void Add(string path)
    {
        lock (_paths)
            _paths.Add(path);
    }
}
