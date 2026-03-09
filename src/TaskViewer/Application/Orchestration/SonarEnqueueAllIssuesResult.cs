namespace TaskViewer.Application.Orchestration;

public sealed record SonarEnqueueAllIssuesResult(
    IReadOnlyList<NormalizedIssue> Issues,
    int Matched,
    bool Truncated);
