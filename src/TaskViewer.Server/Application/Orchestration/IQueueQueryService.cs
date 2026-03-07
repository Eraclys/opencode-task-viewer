using System.Text.Json.Nodes;
using TaskViewer.Server;
using TaskViewer.Server.Infrastructure.Orchestration;

namespace TaskViewer.Server.Application.Orchestration;

internal interface IQueueQueryService
{
    Task<List<QueueItemRecord>> ListQueueAsync(object? states, object? limit);
    Task<QueueStats> GetQueueStatsAsync();
}

internal sealed class QueueQueryService : IQueueQueryService
{
    private readonly IQueueRepository _queueRepository;

    public QueueQueryService(IQueueRepository queueRepository)
    {
        _queueRepository = queueRepository;
    }

    public async Task<List<QueueItemRecord>> ListQueueAsync(object? states, object? limit)
    {
        var selectedStates = NormalizeQueueStateList(states);
        var n = Math.Clamp(ParseIntSafe(limit, 250), 1, 5000);
        return await _queueRepository.ListQueue(selectedStates, n);
    }

    public Task<QueueStats> GetQueueStatsAsync()
    {
        return _queueRepository.GetQueueStats();
    }

    private static int ParseIntSafe(object? value, int fallback)
    {
        if (value is null)
            return fallback;

        if (value is int i)
            return i;

        if (value is long l && l is >= int.MinValue and <= int.MaxValue)
            return (int)l;

        return int.TryParse(value.ToString(), out var parsed) ? parsed : fallback;
    }

    private static List<string> NormalizeQueueStateList(object? states)
    {
        var allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            "queued",
            "dispatching",
            "session_created",
            "done",
            "failed",
            "cancelled"
        };

        var result = new HashSet<string>(StringComparer.Ordinal);

        if (states is JsonArray a)
        {
            foreach (var n in a)
            {
                var v = n?.ToString()?.Trim().ToLowerInvariant();

                if (!string.IsNullOrWhiteSpace(v) && allowed.Contains(v))
                    result.Add(v);
            }

            return [.. result];
        }

        var csv = states?.ToString() ?? string.Empty;

        foreach (var p in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var v = p.ToLowerInvariant();

            if (allowed.Contains(v))
                result.Add(v);
        }

        return [.. result];
    }
}
