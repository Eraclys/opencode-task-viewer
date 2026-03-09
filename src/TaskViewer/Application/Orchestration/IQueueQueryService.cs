using TaskViewer.Infrastructure.Orchestration;

namespace TaskViewer.Application.Orchestration;

public interface IQueueQueryService
{
    Task<List<QueueItemRecord>> ListQueueAsync(string? statesCsv, int? limit);
    Task<QueueStats> GetQueueStatsAsync();
}
