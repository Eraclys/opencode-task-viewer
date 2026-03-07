using TaskViewer.Server.Infrastructure.Orchestration;

namespace TaskViewer.Server.Application.Orchestration;

interface IQueueQueryService
{
    Task<List<QueueItemRecord>> ListQueueAsync(string? statesCsv, string? limit);
    Task<QueueStats> GetQueueStatsAsync();
}
