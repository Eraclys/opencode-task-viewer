using SonarQube.OpenCodeTaskViewer.Domain.Orchestration;

namespace SonarQube.OpenCodeTaskViewer.Server.Tests;

public sealed class DispatchFailurePolicyTests
{
    [Fact]
    public void Decide_ReturnsFailed_WhenAttemptsExhausted()
    {
        var sut = new DispatchFailurePolicy();

        var result = sut.Decide(
            3,
            3,
            new DateTimeOffset(
                2026,
                1,
                1,
                0,
                0,
                0,
                TimeSpan.Zero));

        Assert.Equal(QueueState.Failed, result.State);
        Assert.Null(result.NextAttemptAt);
    }

    [Fact]
    public void Decide_ReturnsQueuedWithBackoff_WhenRetryAllowed()
    {
        var sut = new DispatchFailurePolicy();

        var now = new DateTimeOffset(
            2026,
            1,
            1,
            0,
            0,
            0,
            TimeSpan.Zero);

        var result = sut.Decide(1, 3, now);

        Assert.Equal(QueueState.Queued, result.State);
        Assert.Equal(now.AddMilliseconds(2500), result.NextAttemptAt);
    }

    [Fact]
    public void Decide_CapsBackoffAtSixtySeconds()
    {
        var sut = new DispatchFailurePolicy();

        var now = new DateTimeOffset(
            2026,
            1,
            1,
            0,
            0,
            0,
            TimeSpan.Zero);

        var result = sut.Decide(20, 21, now);

        Assert.Equal(now.AddSeconds(60), result.NextAttemptAt);
    }
}
