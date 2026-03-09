using TaskViewer.Infrastructure.Orchestration;

namespace TaskViewer.Application.Orchestration;

public interface IQueueQueryService
{
    Task<List<QueueItemRecord>> ListQueueAsync(string? statesCsv, string? limit);
    Task<QueueStats> GetQueueStatsAsync();
}
