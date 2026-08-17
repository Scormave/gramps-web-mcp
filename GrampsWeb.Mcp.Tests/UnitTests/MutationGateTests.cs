using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Exceptions;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

public class MutationGateTests
{
    [Fact]
    public async Task Disabled_Allows_Overlapping_Runs()
    {
        var inFlight = 0;
        var maxInFlight = 0;
        var gate = new object();

        async Task Work()
        {
            await MutationGate.Disabled.RunAsync(async () =>
            {
                lock (gate)
                {
                    inFlight++;
                    maxInFlight = Math.Max(maxInFlight, inFlight);
                }

                await Task.Delay(40);
                lock (gate)
                    inFlight--;
            });
        }

        await Task.WhenAll(Work(), Work(), Work());
        Assert.True(maxInFlight > 1);
    }

    [Fact]
    public async Task Serialize_Prevents_Overlapping_Runs()
    {
        var mutationGate = new MutationGate(serialize: true, TimeSpan.Zero, TimeSpan.FromSeconds(5));
        var inFlight = 0;
        var maxInFlight = 0;
        var gate = new object();

        async Task Work()
        {
            await mutationGate.RunAsync(async () =>
            {
                lock (gate)
                {
                    inFlight++;
                    maxInFlight = Math.Max(maxInFlight, inFlight);
                }

                await Task.Delay(30);
                lock (gate)
                    inFlight--;
            });
        }

        await Task.WhenAll(Work(), Work(), Work());
        Assert.Equal(1, maxInFlight);
    }

    [Fact]
    public async Task MinInterval_Is_Respected_Between_Sequential_Runs()
    {
        var mutationGate = new MutationGate(
            serialize: false,
            minInterval: TimeSpan.FromMilliseconds(80),
            acquireTimeout: TimeSpan.FromSeconds(5));
        var stamps = new List<DateTimeOffset>();

        await mutationGate.RunAsync(() =>
        {
            stamps.Add(DateTimeOffset.UtcNow);
            return Task.CompletedTask;
        });
        await mutationGate.RunAsync(() =>
        {
            stamps.Add(DateTimeOffset.UtcNow);
            return Task.CompletedTask;
        });

        Assert.True((stamps[1] - stamps[0]).TotalMilliseconds >= 70);
    }

    [Fact]
    public async Task AcquireTimeout_Throws_Retryable_Error()
    {
        var mutationGate = new MutationGate(
            serialize: true,
            minInterval: TimeSpan.Zero,
            acquireTimeout: TimeSpan.FromMilliseconds(40));
        var started = new TaskCompletionSource();
        var release = new TaskCompletionSource();

        var held = mutationGate.RunAsync(async () =>
        {
            started.SetResult();
            await release.Task;
        });

        await started.Task;
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => mutationGate.RunAsync(() => Task.CompletedTask));

        Assert.Contains("queue timed out", ex.Message);
        Assert.Contains("Retry after", ex.Message);

        release.SetResult();
        await held;
    }

    [Fact]
    public async Task Failed_Run_Still_Updates_LastMutation_Timestamp()
    {
        var mutationGate = new MutationGate(
            serialize: true,
            minInterval: TimeSpan.FromMilliseconds(60),
            acquireTimeout: TimeSpan.FromSeconds(5));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => mutationGate.RunAsync(() => throw new InvalidOperationException("boom")));

        var started = DateTimeOffset.UtcNow;
        await mutationGate.RunAsync(() => Task.CompletedTask);
        Assert.True((DateTimeOffset.UtcNow - started).TotalMilliseconds >= 40);
    }
}
