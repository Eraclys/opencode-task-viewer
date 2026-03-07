using System.Text.Json.Nodes;
using TaskViewer.Server;
using TaskViewer.Server.Infrastructure.Orchestration;

namespace TaskViewer.Server.Application.Orchestration;

internal interface IQueueEnqueueService
{
    Task<QueueEnqueueBatchResult> EnqueueRawIssuesAsync(
        MappingRecord mapping,
        string? type,
        string instructionText,
        IReadOnlyList<JsonNode?> rawIssues);
}

internal sealed record QueueEnqueueBatchResult(
    List<QueueItemRecord> CreatedItems,
    List<QueueEnqueueSkipView> Skipped);

internal sealed class QueueEnqueueService : IQueueEnqueueService
{
    private readonly IQueueRepository _queueRepository;
    private readonly int _maxAttempts;
    private readonly Func<string> _nowIso;

    public QueueEnqueueService(
        IQueueRepository queueRepository,
        int maxAttempts,
        Func<string>? nowIso = null)
    {
        _queueRepository = queueRepository;
        _maxAttempts = maxAttempts;
        _nowIso = nowIso ?? (() => DateTimeOffset.UtcNow.ToString("O"));
    }

    public async Task<QueueEnqueueBatchResult> EnqueueRawIssuesAsync(
        MappingRecord mapping,
        string? type,
        string instructionText,
        IReadOnlyList<JsonNode?> rawIssues)
    {
        var normalizedIssues = new List<NormalizedIssue>();
        var skipped = new List<QueueEnqueueSkipView>();

        foreach (var rawIssue in rawIssues)
        {
            var issue = SonarIssueNormalizer.NormalizeForQueue(rawIssue, mapping);

            if (issue is null)
            {
                skipped.Add(OrchestrationResponseMapper.BuildInvalidIssueSkip());
                continue;
            }

            normalizedIssues.Add(issue);
        }

        var (createdItems, repoSkipped) = await _queueRepository.EnqueueIssuesBatch(
            mapping,
            type,
            instructionText,
            normalizedIssues,
            _maxAttempts,
            _nowIso());

        foreach (var item in repoSkipped)
            skipped.Add(OrchestrationResponseMapper.BuildRepoSkip(item));

        return new QueueEnqueueBatchResult(createdItems, skipped);
    }
}
