using TaskViewer.Infrastructure.Persistence;

namespace TaskViewer.Domain.Orchestration;

public sealed record SonarEnqueueAllIssuesResult(
    IReadOnlyList<NormalizedIssue> Issues,
    int Matched,
    bool Truncated);
