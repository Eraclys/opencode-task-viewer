using System.Globalization;
using TaskViewer.Infrastructure.Persistence;

namespace TaskViewer.Domain.Orchestration;

public sealed class QueueWorkerCoordinator : IQueueWorkerCoordinator
{
    public async Task ScheduleAsync(
        HashSet<string> inFlight,
        int maxActive,
        Func<Task<QueueItemRecord?>> claimNext,
        Func<QueueItemRecord, Task> dispatch,
        Action onChange)
    {
        if (maxActive <= 0)
            return;

        while (inFlight.Count < maxActive)
        {
            var claim = await claimNext();

            if (claim is null)
                break;

            var key = claim.Id.ToString(CultureInfo.InvariantCulture);
            inFlight.Add(key);

            _ = dispatch(claim)
                .ContinueWith(_ =>
                {
                    inFlight.Remove(key);
                    onChange();
                });
        }
    }
}
