using TaskViewer.Server.Application.Orchestration;

namespace TaskViewer.Server.Tests;

public sealed class DispatchFailurePolicyTests
{
    [Fact]
    public void Decide_ReturnsFailed_WhenAttemptsExhausted()
    {
        var sut = new DispatchFailurePolicy();

        var result = sut.Decide(attemptCount: 3, maxAttempts: 3, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

        Assert.Equal("failed", result.State);
        Assert.Null(result.NextAttemptAt);
    }

    [Fact]
    public void Decide_ReturnsQueuedWithBackoff_WhenRetryAllowed()
    {
        var sut = new DispatchFailurePolicy();
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var result = sut.Decide(attemptCount: 1, maxAttempts: 3, now);

        Assert.Equal("queued", result.State);
        Assert.Equal(now.AddMilliseconds(2500).ToString("O"), result.NextAttemptAt);
    }

    [Fact]
    public void Decide_CapsBackoffAtSixtySeconds()
    {
        var sut = new DispatchFailurePolicy();
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var result = sut.Decide(attemptCount: 20, maxAttempts: 21, now);

        Assert.Equal(now.AddSeconds(60).ToString("O"), result.NextAttemptAt);
    }
}
