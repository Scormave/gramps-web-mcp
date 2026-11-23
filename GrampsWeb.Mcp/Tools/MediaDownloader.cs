using System.Net;
using System.Net.Sockets;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Config;
using GrampsWeb.Mcp.Input;

namespace GrampsWeb.Mcp.Tools;

/// <summary>
/// Downloads a file attached in the chat (<see cref="FileReference"/>) for create_media and update_media.
/// Only HTTPS links on the hosts <c>GRAMPS_MEDIA_UPLOAD_HOSTS</c> allows are fetched, redirects included;
/// connections go only to public addresses, so a link cannot reach the server's own network; no cookies or
/// credentials are sent; and the upload size limit applies while the file is read. Messages name the link's
/// host, never the link: download links are signed.
/// </summary>
public sealed class MediaDownloader : IDisposable
{
    internal const int MaxRedirects = 3;

    internal static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    private readonly HttpClient _client;

    public MediaDownloader()
        : this(new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseProxy = false,
            UseCookies = false,
            AutomaticDecompression = DecompressionMethods.None,
            ConnectCallback = ConnectToPublicAddressAsync,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        })
    {
    }

    /// <summary>For tests: <paramref name="handler"/> must not follow redirects itself.</summary>
    internal MediaDownloader(HttpMessageHandler handler)
    {
        _client = new HttpClient(handler) { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
        GrampsUserAgent.Configure(_client);
    }

    public void Dispose() => _client.Dispose();

    /// <summary>
    /// Downloads <paramref name="file"/> and checks it as an upload: its size against
    /// <c>GRAMPS_MEDIA_UPLOAD_MAX_BYTES</c> and its type by content. The upload is named by the file's name,
    /// or by its ID when the client sent no name.
    /// </summary>
    internal async Task<MediaUploadFile> ReadAsync(FileReference file, GrampsConfig config)
    {
        if (!config.MediaUploadFromLinksEnabled)
            throw McpToolErrors.ValidationError("Uploading a file attached in the chat is turned off: " + (
                config.MediaUploadHosts is not { Count: > 0 }
                    ? "set GRAMPS_MEDIA_UPLOAD_HOSTS to the hosts the chat client serves attached files from"
                      + (config.MediaResourcesEnabled ? "." : ", and GRAMPS_MEDIA_RESOURCES_ENABLED=true.")
                      + (config.MediaUploadFromFoldersEnabled ? " A file in the server's upload folder goes in filePath instead." : "")
                    : "media file access is disabled; set GRAMPS_MEDIA_RESOURCES_ENABLED=true."));
        if (string.IsNullOrWhiteSpace(file.DownloadUrl))
            throw McpToolErrors.ValidationError(
                "file needs download_url: pass the file object the chat client gives for an attached file, not its name or ID.");

        var name = NameOf(file);
        using var timeout = new CancellationTokenSource(Timeout);
        try
        {
            return await DownloadAsync(name, CheckLink(file.DownloadUrl, config, name, redirect: false), config, timeout.Token);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            throw McpToolErrors.ValidationError(
                $"Downloading {name} did not finish in {Timeout.TotalSeconds:0} seconds. Try again, or attach a smaller file.");
        }
    }

    private async Task<MediaUploadFile> DownloadAsync(string name, Uri link, GrampsConfig config, CancellationToken cancellationToken)
    {
        for (var redirects = 0; ; redirects++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, link);
            HttpResponseMessage response;
            try
            {
                response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            }
            catch (HttpRequestException ex) when (ex.InnerException is NonPublicAddressException)
            {
                throw McpToolErrors.ValidationError(
                    $"{name} was not downloaded: {link.Host} resolves only to private or local addresses, which the server does not connect to.");
            }
            catch (HttpRequestException ex)
            {
                throw McpToolErrors.ValidationError($"Downloading {name} from {link.Host} failed: {ex.Message}");
            }

            using (response)
            {
                if (IsRedirect(response.StatusCode))
                {
                    if (redirects == MaxRedirects)
                        throw McpToolErrors.ValidationError(
                            $"{name} was not downloaded: {link.Host} redirected more than {MaxRedirects} times.");
                    var location = response.Headers.Location
                        ?? throw McpToolErrors.ValidationError($"{name} was not downloaded: {link.Host} redirected without saying where.");
                    link = CheckLink(new Uri(link, location).AbsoluteUri, config, name, redirect: true);
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                    throw McpToolErrors.ValidationError(
                        $"Downloading {name} failed: {link.Host} answered {(int)response.StatusCode} {response.ReasonPhrase}. " +
                        "The link may have expired; attach the file in the chat again.");
                if (response.Content.Headers.ContentLength is { } length && length > config.MediaUploadMaxBytes)
                    throw MediaUploadFile.FileTooLarge(name, length, config.MediaUploadMaxBytes);

                var bytes = await ReadLimitedAsync(response.Content, name, config.MediaUploadMaxBytes, cancellationToken);
                if (bytes.Length == 0)
                    throw McpToolErrors.ValidationError($"{name} is empty.");
                return MediaUploadFile.FromBytes(name, bytes);
            }
        }
    }

    /// <summary>
    /// The link when it is HTTPS on an allowed host; otherwise an error naming the host. The host is matched
    /// in its ASCII form, so a look-alike Unicode name cannot pass for an allowed one.
    /// </summary>
    internal static Uri CheckLink(string value, GrampsConfig config, string name, bool redirect)
    {
        var what = redirect ? "redirects to" : "is";
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var link))
            throw McpToolErrors.ValidationError($"{name} was not downloaded: download_url is not a link.");
        if (link.Scheme != Uri.UriSchemeHttps)
            throw McpToolErrors.ValidationError($"{name} was not downloaded: it {what} a {link.Scheme} link, and only HTTPS links are fetched.");
        if (link.UserInfo.Length > 0)
            throw McpToolErrors.ValidationError($"{name} was not downloaded: links with a user name or password are not fetched.");

        var host = link.IdnHost.TrimEnd('.').ToLowerInvariant();
        var hosts = config.MediaUploadHosts ?? [];
        if (link.HostNameType != UriHostNameType.Dns || !IsAllowedHost(host, hosts))
            throw McpToolErrors.ValidationError(
                $"{name} was not downloaded: it {what} a link on {host}, which GRAMPS_MEDIA_UPLOAD_HOSTS does not allow " +
                $"({string.Join(", ", hosts)}). If the chat client serves attached files from {host}, add it to the setting.");
        return link;
    }

    /// <summary>
    /// Whether <paramref name="host"/> is one of <paramref name="hosts"/> or a subdomain of one. A <c>*</c> in
    /// an entry's first label stands for any letters, digits or hyphens in that label, never a dot: an entry
    /// <c>files-*.example.com</c> allows files-eu.example.com and its subdomains, not files.example.com.evil.test.
    /// </summary>
    internal static bool IsAllowedHost(string host, IEnumerable<string> hosts) =>
        hosts.Any(allowed => allowed.Contains('*') ? MatchesPattern(host, allowed) : IsSameOrSubdomain(host, allowed));

    private static bool IsSameOrSubdomain(string host, string allowed) =>
        host == allowed
        || (host.Length > allowed.Length + 1
            && host.EndsWith(allowed, StringComparison.Ordinal)
            && host[^(allowed.Length + 1)] == '.');

    /// <summary>
    /// Whether <paramref name="host"/> matches <paramref name="pattern"/>, such as <c>files-*.example.com</c>,
    /// or is a subdomain of a match.
    /// </summary>
    private static bool MatchesPattern(string host, string pattern)
    {
        var dot = pattern.IndexOf('.');
        var rest = pattern[dot..];
        if (!host.EndsWith(rest, StringComparison.Ordinal))
            return false;
        var head = host[..^rest.Length];
        var label = head[(head.LastIndexOf('.') + 1)..];
        var star = pattern.IndexOf('*');
        var prefix = pattern[..star];
        var suffix = pattern[(star + 1)..dot];
        return label.Length > 0
               && label.Length >= prefix.Length + suffix.Length
               && label.StartsWith(prefix, StringComparison.Ordinal)
               && label.EndsWith(suffix, StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether the server may connect to <paramref name="address"/>: a public unicast address, not loopback,
    /// private, link-local, carrier-grade NAT, multicast, documentation or another reserved range. IPv6
    /// forms that carry an IPv4 address (mapped, 6to4, NAT64) are judged by that address.
    /// </summary>
    internal static bool IsPublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();
        var b = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
            return !(b[0] is 0 or 10 or 127 or >= 224
                     || (b[0] == 100 && (b[1] & 0xC0) == 64)
                     || (b[0] == 169 && b[1] == 254)
                     || (b[0] == 172 && (b[1] & 0xF0) == 16)
                     || (b[0] == 192 && b[1] == 168)
                     || (b[0] == 192 && b[1] == 0 && b[2] is 0 or 2)
                     || (b[0] == 198 && (b[1] & 0xFE) == 18)
                     || (b[0] == 198 && b[1] == 51 && b[2] == 100)
                     || (b[0] == 203 && b[1] == 0 && b[2] == 113));
        if (address.AddressFamily != AddressFamily.InterNetworkV6)
            return false;
        if (b[0] == 0x20 && b[1] == 0x02)
            return IsPublicAddress(new IPAddress(b.AsSpan(2, 4)));
        if (b.AsSpan(0, 12).SequenceEqual((ReadOnlySpan<byte>)[0x00, 0x64, 0xFF, 0x9B, 0, 0, 0, 0, 0, 0, 0, 0]))
            return IsPublicAddress(new IPAddress(b.AsSpan(12, 4)));
        if (b[0] == 0x20 && b[1] == 0x01 && b[2] == 0x0D && b[3] == 0xB8)
            return false;
        // Global unicast is 2000::/3; loopback, ULA, link-local, multicast and the rest lie outside it.
        return (b[0] & 0xE0) == 0x20;
    }

    /// <summary>The upload's name: the attached file's name without any folder, else its ID.</summary>
    private static string NameOf(FileReference file)
    {
        var name = Path.GetFileName((file.FileName ?? "").Replace('\\', '/'));
        name = new string(name.Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (name.Length == 0 || name is "." or "..")
            name = string.IsNullOrWhiteSpace(file.FileId) ? "attached file" : file.FileId.Trim();
        return name.Length > 200 ? name[..200] : name;
    }

    private static bool IsRedirect(HttpStatusCode status) =>
        status is HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.SeeOther
            or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;

    private static async Task<byte[]> ReadLimitedAsync(HttpContent content, string name, long maxBytes, CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > maxBytes)
                throw McpToolErrors.ValidationError(
                    $"{name} is more than the upload limit of {maxBytes} bytes (GRAMPS_MEDIA_UPLOAD_MAX_BYTES).");
            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    /// <summary>
    /// Resolves the host and connects only to its public addresses, so the address checked is the address
    /// connected to, and a name that resolves to the server's own network is refused.
    /// </summary>
    private static async ValueTask<Stream> ConnectToPublicAddressAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancellationToken);
        var allowed = addresses.Where(IsPublicAddress).ToArray();
        if (allowed.Length == 0)
            throw new NonPublicAddressException();

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(allowed, context.DnsEndPoint.Port, cancellationToken);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private sealed class NonPublicAddressException() : IOException("The host resolves only to private or local addresses.");
}
