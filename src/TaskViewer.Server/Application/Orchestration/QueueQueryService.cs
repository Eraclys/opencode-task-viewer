using TaskViewer.Server.Infrastructure.Orchestration;

namespace TaskViewer.Server.Application.Orchestration;

sealed class QueueQueryService : IQueueQueryService
{
    readonly IQueueRepository _queueRepository;

    public QueueQueryService(IQueueRepository queueRepository)
    {
        _queueRepository = queueRepository;
    }

    public async Task<List<QueueItemRecord>> ListQueueAsync(string? statesCsv, string? limit)
    {
        var selectedStates = NormalizeQueueStateList(statesCsv);
        var n = Math.Clamp(ParseIntSafe(limit, 250), 1, 5000);

        return await _queueRepository.ListQueue(selectedStates, n);
    }

    public Task<QueueStats> GetQueueStatsAsync() => _queueRepository.GetQueueStats();

    static int ParseIntSafe(string? value, int fallback)
    {
        return int.TryParse(value, out var parsed) ? parsed : fallback;
    }

    static List<string> NormalizeQueueStateList(string? statesCsv)
    {
        var allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            "queued",
            "dispatching",
            "leased",
            "running",
            "awaiting_review",
            "rejected",
            "session_created",
            "done",
            "failed",
            "cancelled"
        };

        var result = new HashSet<string>(StringComparer.Ordinal);

        var csv = statesCsv ?? string.Empty;

        foreach (var p in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var v = p.ToLowerInvariant();

            if (allowed.Contains(v))
                result.Add(v);
        }

        return [.. result];
    }
}
