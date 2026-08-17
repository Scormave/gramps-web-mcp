using GrampsWeb.Mcp.Config;
using GrampsWeb.Mcp.Exceptions;

namespace GrampsWeb.Mcp.Client;

/// <summary>
/// Process-wide write policy for Gramps mutations: optional single-flight lock and
/// minimum interval between create/update/delete HTTP calls.
/// </summary>
public sealed class MutationGate
{
    public static readonly TimeSpan DefaultAcquireTimeout = TimeSpan.FromSeconds(30);

    public static readonly MutationGate Disabled = new(
        serialize: false,
        minInterval: TimeSpan.Zero,
        acquireTimeout: DefaultAcquireTimeout);

    private readonly SemaphoreSlim _mutex = new(1, 1);
    private readonly bool _enabled;
    private readonly TimeSpan _minInterval;
    private readonly TimeSpan _acquireTimeout;
    private DateTimeOffset _lastMutationUtc = DateTimeOffset.MinValue;

    public MutationGate(GrampsConfig config)
        : this(
            config.MutationSerialize,
            TimeSpan.FromMilliseconds(config.MutationMinIntervalMs),
            DefaultAcquireTimeout)
    {
    }

    public MutationGate(bool serialize, TimeSpan minInterval, TimeSpan acquireTimeout)
    {
        if (minInterval < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(minInterval));
        if (acquireTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(acquireTimeout));

        _minInterval = minInterval;
        _acquireTimeout = acquireTimeout;
        _enabled = serialize || minInterval > TimeSpan.Zero;
    }

    public bool IsEnabled => _enabled;

    public Task RunAsync(Func<Task> action) =>
        RunAsync(async () =>
        {
            await action().ConfigureAwait(false);
            return 0;
        });

    public async Task<T> RunAsync<T>(Func<Task<T>> action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (!_enabled)
            return await action().ConfigureAwait(false);

        var entered = await _mutex.WaitAsync(_acquireTimeout).ConfigureAwait(false);
        if (!entered)
            throw new InvalidOperationException(GrampsRetryableWriteErrors.GateTimeout());

        try
        {
            await WaitMinIntervalAsync().ConfigureAwait(false);
            return await action().ConfigureAwait(false);
        }
        finally
        {
            _lastMutationUtc = DateTimeOffset.UtcNow;
            _mutex.Release();
        }
    }

    private async Task WaitMinIntervalAsync()
    {
        if (_minInterval <= TimeSpan.Zero)
            return;

        var elapsed = DateTimeOffset.UtcNow - _lastMutationUtc;
        var remaining = _minInterval - elapsed;
        if (remaining > TimeSpan.Zero)
            await Task.Delay(remaining).ConfigureAwait(false);
    }
}
