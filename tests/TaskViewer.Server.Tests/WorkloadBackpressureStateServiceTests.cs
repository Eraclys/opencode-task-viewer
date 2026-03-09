using TaskViewer.Application.Orchestration;

namespace TaskViewer.Server.Tests;

public sealed class WorkloadBackpressureStateServiceTests
{
    [Fact]
    public async Task EvaluateAsync_DisabledLimit_ReturnsUnpausedWithoutSampling()
    {
        var working = new FakeWorkingSessionsReadService(7, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var sut = new WorkloadBackpressureStateService(working, new WorkloadBackpressurePolicy());
        var onChangeCount = 0;

        var state = await sut.EvaluateAsync(
            forceRefresh: false,
            maxWorkingGlobal: 0,
            workingResumeBelow: 0,
            pollMs: 1000,
            onStateChanged: () => onChangeCount += 1);

        Assert.False(state.Paused);
        Assert.Equal(0, state.WorkingCount);
        Assert.Equal(0, working.CallCount);
        Assert.Equal(0, onChangeCount);
    }

    [Fact]
    public async Task EvaluateAsync_TransitionsPausedState_AndNotifiesOnlyOnChange()
    {
        var working = new FakeWorkingSessionsReadService(10, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var sut = new WorkloadBackpressureStateService(working, new WorkloadBackpressurePolicy());
        var onChangeCount = 0;

        var first = await sut.EvaluateAsync(true, maxWorkingGlobal: 10, workingResumeBelow: 5, pollMs: 1000, () => onChangeCount += 1);
        var second = await sut.EvaluateAsync(true, maxWorkingGlobal: 10, workingResumeBelow: 5, pollMs: 1000, () => onChangeCount += 1);

        working.Count = 4;
        var third = await sut.EvaluateAsync(true, maxWorkingGlobal: 10, workingResumeBelow: 5, pollMs: 1000, () => onChangeCount += 1);

        Assert.True(first.Paused);
        Assert.True(second.Paused);
        Assert.False(third.Paused);
        Assert.Equal(2, onChangeCount);
    }

    [Fact]
    public async Task EvaluateAsync_PreservesLatestSampleTimestamp_WhenDisabledAfterSampling()
    {
        var sampleAt = new DateTimeOffset(2026, 2, 3, 4, 5, 6, TimeSpan.Zero);
        var working = new FakeWorkingSessionsReadService(3, sampleAt);
        var sut = new WorkloadBackpressureStateService(working, new WorkloadBackpressurePolicy());

        var sampled = await sut.EvaluateAsync(true, maxWorkingGlobal: 10, workingResumeBelow: 5, pollMs: 1000, () => { });
        var disabled = await sut.EvaluateAsync(false, maxWorkingGlobal: 0, workingResumeBelow: 0, pollMs: 1000, () => { });

        Assert.Equal(sampled.SampleAt, disabled.SampleAt);
        Assert.Equal(sampleAt, disabled.SampleAt);
    }

    private sealed class FakeWorkingSessionsReadService : IWorkingSessionsReadService
    {
        public int Count { get; set; }
        public DateTimeOffset SampleAt { get; set; }
        public int CallCount { get; private set; }

        public FakeWorkingSessionsReadService(int count, DateTimeOffset sampleAt)
        {
            Count = count;
            SampleAt = sampleAt;
        }

        public Task<WorkingSessionsSample> GetWorkingSessionsCountAsync(bool forceRefresh, int pollMs)
        {
            CallCount += 1;
            return Task.FromResult(new WorkingSessionsSample(SampleAt, Count));
        }
    }
}
