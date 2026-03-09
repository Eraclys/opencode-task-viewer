using TaskViewer.Application.Orchestration;

namespace TaskViewer.Server.Tests;

public sealed class QueueWorkerCoordinatorTests
{
    [Fact]
    public async Task ScheduleAsync_ClaimsUpToMaxActive()
    {
        var sut = new QueueWorkerCoordinator();
        var inFlight = new HashSet<string>(StringComparer.Ordinal);

        var claims = new Queue<QueueItemRecord?>(
        [
            new QueueItemRecord
            {
                Id = 1,
                IssueKey = "sq-1",
                MappingId = 1,
                SonarProjectKey = "k",
                Directory = "C:/Work",
                CreatedAt = DateTimeOffset.UnixEpoch,
                UpdatedAt = DateTimeOffset.UnixEpoch
            },
            new QueueItemRecord
            {
                Id = 2,
                IssueKey = "sq-2",
                MappingId = 1,
                SonarProjectKey = "k",
                Directory = "C:/Work",
                CreatedAt = DateTimeOffset.UnixEpoch,
                UpdatedAt = DateTimeOffset.UnixEpoch
            },
            new QueueItemRecord
            {
                Id = 3,
                IssueKey = "sq-3",
                MappingId = 1,
                SonarProjectKey = "k",
                Directory = "C:/Work",
                CreatedAt = DateTimeOffset.UnixEpoch,
                UpdatedAt = DateTimeOffset.UnixEpoch
            }
        ]);

        var heldTasks = new List<TaskCompletionSource<bool>>();
        var dispatched = new List<int>();

        await sut.ScheduleAsync(
            inFlight,
            2,
            () => Task.FromResult(claims.Count > 0 ? claims.Dequeue() : null),
            item =>
            {
                dispatched.Add(item.Id);
                var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                heldTasks.Add(tcs);

                return tcs.Task;
            },
            () => { });

        Assert.Equal(
            [
                1,
                2
            ],
            dispatched);

        Assert.Equal(2, inFlight.Count);
    }

    [Fact]
    public async Task ScheduleAsync_RemovesCompletedDispatchAndRaisesOnChange()
    {
        var sut = new QueueWorkerCoordinator();
        var inFlight = new HashSet<string>(StringComparer.Ordinal);
        var onChangeSignal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var claims = new Queue<QueueItemRecord?>(
        [
            new QueueItemRecord
            {
                Id = 11,
                IssueKey = "sq-11",
                MappingId = 1,
                SonarProjectKey = "k",
                Directory = "C:/Work",
                CreatedAt = DateTimeOffset.UnixEpoch,
                UpdatedAt = DateTimeOffset.UnixEpoch
            },
            null
        ]);

        await sut.ScheduleAsync(
            inFlight,
            1,
            () => Task.FromResult(claims.Count > 0 ? claims.Dequeue() : null),
            _ => Task.CompletedTask,
            () => onChangeSignal.TrySetResult(true));

        await onChangeSignal.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Empty(inFlight);
    }
}
