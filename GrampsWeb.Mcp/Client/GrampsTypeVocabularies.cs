using System.Collections.Concurrent;

namespace GrampsWeb.Mcp.Client;

/// <summary>
/// Keeps Gramps type vocabularies between tool calls. Default types come with the Gramps
/// version, so they stay for the process lifetime; custom types change when someone adds one
/// in Gramps, so they expire after <see cref="CustomTypesTtl"/>. Failed reads are not kept.
/// </summary>
public sealed class GrampsTypeVocabularies(TimeProvider? timeProvider = null)
{
    public static readonly TimeSpan CustomTypesTtl = TimeSpan.FromMinutes(10);

    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly ConcurrentDictionary<string, Lazy<Task<string>>> _defaults = new();
    private readonly Lock _customLock = new();
    private Lazy<Task<string>>? _custom;
    private long _customStartedAt;

    /// <summary>Reads one default-types path once, sharing in-flight reads.</summary>
    internal async Task<string> GetDefaultAsync(string path, Func<Task<string>> fetch)
    {
        var read = _defaults.GetOrAdd(path, _ => new Lazy<Task<string>>(fetch));
        try
        {
            return await read.Value.ConfigureAwait(false);
        }
        catch
        {
            // Failures must remain retryable; remove only this failed generation.
            _defaults.TryRemove(new KeyValuePair<string, Lazy<Task<string>>>(path, read));
            throw;
        }
    }

    /// <summary>
    /// Reads custom types, again after <see cref="CustomTypesTtl"/> or a failure. With
    /// <paramref name="reload"/>, a finished read is replaced; an in-flight one is shared.
    /// </summary>
    internal Task<string> GetCustomAsync(Func<Task<string>> fetch, bool reload = false)
    {
        Lazy<Task<string>> read;
        lock (_customLock)
        {
            var current = _custom;
            var finished = current is { IsValueCreated: true, Value.IsCompleted: true };
            if (current is null || finished && (reload
                || !current.Value.IsCompletedSuccessfully
                || _time.GetElapsedTime(_customStartedAt) >= CustomTypesTtl))
            {
                current = new Lazy<Task<string>>(fetch);
                _custom = current;
                _customStartedAt = _time.GetTimestamp();
            }

            read = current;
        }

        return read.Value;
    }
}
