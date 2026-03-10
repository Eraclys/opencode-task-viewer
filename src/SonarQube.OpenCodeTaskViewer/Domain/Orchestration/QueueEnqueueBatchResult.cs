using SonarQube.OpenCodeTaskViewer.Infrastructure.Persistence;

namespace SonarQube.OpenCodeTaskViewer.Domain.Orchestration;

public sealed record QueueEnqueueBatchResult(
    List<QueueItemRecord> CreatedItems,
    List<QueueEnqueueSkipView> Skipped);
