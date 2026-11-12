using System.Globalization;
using System.Text.Json;
using GrampsWeb.Mcp.Exceptions;

namespace GrampsWeb.Mcp.Client;

/// <summary>
/// The oldest Gramps Web API this server works with. <see cref="GrampsApiClient"/> checks the connected
/// server once per process, before its first request, so an older server gets one clear refusal instead of
/// failures deep inside a tool. A server that does not report its version is let through, and so is a
/// request whose version read failed: the request then reports the server's error as its own.
/// </summary>
public sealed class GrampsServerVersion
{
    /// <summary>Gramps Web API 3.16 added <c>POST /api/objects/delete-by-handle/</c>.</summary>
    public static readonly Version Minimum = new(3, 16);

    /// <summary>The oldest Gramps Web release known to bundle <see cref="Minimum"/>.</summary>
    public const string MinimumGrampsWebRelease = "26.6.2";

    internal const string MetadataPath = "/api/metadata/";

    private readonly Lock _lock = new();
    private Lazy<Task<string>>? _read;
    private volatile bool _supported;

    /// <summary>
    /// Returns once the server reported a supported version, or none; throws
    /// <see cref="UnsupportedGrampsServerException"/> for an older one. Concurrent callers share one read of
    /// <c>/api/metadata/</c>. A failed read returns without an answer, so the caller's own request meets the
    /// server's error and reports it. A failed read or an older version is not kept: the next request reads
    /// again, and an upgraded server passes without a restart.
    /// </summary>
    internal async Task EnsureSupportedAsync(Func<Task<string>> readMetadata)
    {
        if (_supported)
            return;

        Lazy<Task<string>> read;
        lock (_lock)
            read = _read ??= new Lazy<Task<string>>(readMetadata);

        try
        {
            string metadata;
            try
            {
                metadata = await read.Value.ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is GrampsApiException or HttpRequestException or TaskCanceledException)
            {
                return;
            }

            if (UnsupportedMessage(ReadApiVersion(metadata)) is { } message)
                throw new UnsupportedGrampsServerException($"{message} Nothing was read or changed.");
            _supported = true;
        }
        finally
        {
            if (!_supported)
            {
                lock (_lock)
                {
                    if (ReferenceEquals(_read, read))
                        _read = null;
                }
            }
        }
    }

    /// <summary>
    /// Why a server reporting <paramref name="apiVersion"/> is not supported; null when it is, or when the
    /// version is unknown.
    /// </summary>
    public static string? UnsupportedMessage(string? apiVersion) =>
        apiVersion is not null && Parse(apiVersion) is { } version && version < Minimum
            ? $"This Gramps Web server runs Gramps Web API {apiVersion.Trim()}, and Gramps Web MCP needs " +
              $"{Minimum} or newer. Update Gramps Web ({MinimumGrampsWebRelease} or later), or keep Gramps " +
              "Web MCP 2.x until then."
            : null;

    /// <summary><c>gramps_webapi.version</c> of a <c>/api/metadata/</c> reply, such as "3.24.0"; null without one.</summary>
    public static string? ReadApiVersion(JsonElement metadata) =>
        metadata.ValueKind == JsonValueKind.Object
        && metadata.TryGetProperty("gramps_webapi", out var api) && api.ValueKind == JsonValueKind.Object
        && api.TryGetProperty("version", out var version) && version.ValueKind == JsonValueKind.String
            ? version.GetString()
            : null;

    private static string? ReadApiVersion(string metadataJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(metadataJson);
            return ReadApiVersion(doc.RootElement);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Major and minor of a version such as "3.22.1" or "3.23.0rc1"; null when it has none.</summary>
    public static Version? Parse(string version)
    {
        var parts = version.Trim().TrimStart('v').Split('.');
        return parts.Length >= 2
               && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var major)
               && int.TryParse(new string(parts[1].TakeWhile(char.IsAsciiDigit).ToArray()), NumberStyles.None,
                   CultureInfo.InvariantCulture, out var minor)
            ? new Version(major, minor)
            : null;
    }
}
