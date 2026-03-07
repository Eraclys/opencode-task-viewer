using TaskViewer.Server.Application.Orchestration;

namespace TaskViewer.Server.Tests;

public sealed class OrchestratorRuntimeTests
{
    [Fact]
    public async Task RunOnceAsync_SkipsOverlappingExecution()
    {
        await using var runtime = new OrchestratorRuntime();
        var entered = 0;
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var first = runtime.RunOnceAsync(async () =>
        {
            Interlocked.Increment(ref entered);
            await gate.Task;
        });

        await Task.Delay(20);
        await runtime.RunOnceAsync(() =>
        {
            Interlocked.Increment(ref entered);
            return Task.CompletedTask;
        });

        gate.TrySetResult(true);
        await first;

        Assert.Equal(1, entered);
    }

    [Fact]
    public async Task Start_RunsImmediateTickAndPeriodicTicks()
    {
        await using var runtime = new OrchestratorRuntime();
        using var cts = new CancellationTokenSource();
        var tickCount = 0;

        runtime.Start(
            pollMs: 25,
            cts.Token,
            () =>
            {
                Interlocked.Increment(ref tickCount);
                return Task.CompletedTask;
            });

        await Task.Delay(90);
        cts.Cancel();
        await runtime.DisposeAsync();

        Assert.True(tickCount >= 2);
    }
}
