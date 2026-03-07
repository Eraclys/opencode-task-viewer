using TaskViewer.Server.Infrastructure.Orchestration;

namespace TaskViewer.Server.Application.Orchestration;

internal sealed class QueueEnqueueService : IQueueEnqueueService
{
    private readonly IQueueRepository _queueRepository;
    private readonly int _maxAttempts;
    private readonly Func<DateTimeOffset> _nowUtc;

    public QueueEnqueueService(
        IQueueRepository queueRepository,
        int maxAttempts,
        Func<DateTimeOffset>? nowUtc = null)
    {
        _queueRepository = queueRepository;
        _maxAttempts = maxAttempts;
        _nowUtc = nowUtc ?? (() => DateTimeOffset.UtcNow);
    }

    public async Task<QueueEnqueueBatchResult> EnqueueRawIssuesAsync(
        MappingRecord mapping,
        string? type,
        string instructionText,
        IReadOnlyList<NormalizedIssue> issues)
    {
        var skipped = new List<QueueEnqueueSkipView>();

        var (createdItems, repoSkipped) = await _queueRepository.EnqueueIssuesBatch(
            mapping,
            type,
            instructionText,
            issues,
            _maxAttempts,
            _nowUtc());

        foreach (var item in repoSkipped)
            skipped.Add(OrchestrationResponseMapper.BuildRepoSkip(item));

        return new QueueEnqueueBatchResult(createdItems, skipped);
    }
}
