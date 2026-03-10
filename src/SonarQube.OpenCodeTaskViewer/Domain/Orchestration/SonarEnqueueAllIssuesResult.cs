using SonarQube.OpenCodeTaskViewer.Infrastructure.Persistence;

namespace SonarQube.OpenCodeTaskViewer.Domain.Orchestration;

public sealed record SonarEnqueueAllIssuesResult(
    IReadOnlyList<NormalizedIssue> Issues,
    int Matched,
    bool Truncated);
