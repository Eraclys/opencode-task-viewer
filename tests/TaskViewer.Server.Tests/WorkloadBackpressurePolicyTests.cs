using TaskViewer.Server.Application.Orchestration;

namespace TaskViewer.Server.Tests;

public sealed class WorkloadBackpressurePolicyTests
{
    [Fact]
    public void Evaluate_PausesWhenCountReachesOrExceedsLimit()
    {
        var sut = new WorkloadBackpressurePolicy();

        var transition = sut.Evaluate(
            currentlyPaused: false,
            workingCount: 10,
            maxWorkingGlobal: 10,
            workingResumeBelow: 5);

        Assert.True(transition.NextPaused);
        Assert.True(transition.Changed);
    }

    [Fact]
    public void Evaluate_ResumesOnlyBelowResumeThreshold()
    {
        var sut = new WorkloadBackpressurePolicy();

        var stillPaused = sut.Evaluate(
            currentlyPaused: true,
            workingCount: 5,
            maxWorkingGlobal: 10,
            workingResumeBelow: 5);

        var resumed = sut.Evaluate(
            currentlyPaused: true,
            workingCount: 4,
            maxWorkingGlobal: 10,
            workingResumeBelow: 5);

        Assert.True(stillPaused.NextPaused);
        Assert.False(stillPaused.Changed);
        Assert.False(resumed.NextPaused);
        Assert.True(resumed.Changed);
    }

    [Fact]
    public void Evaluate_KeepsCurrentStateWhenNoThresholdCrossed()
    {
        var sut = new WorkloadBackpressurePolicy();

        var running = sut.Evaluate(false, 2, 10, 5);
        var paused = sut.Evaluate(true, 9, 10, 5);

        Assert.False(running.NextPaused);
        Assert.False(running.Changed);
        Assert.True(paused.NextPaused);
        Assert.False(paused.Changed);
    }
}
