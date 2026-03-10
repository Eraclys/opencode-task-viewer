using TaskViewer.SonarQube;

namespace TaskViewer.Infrastructure.Orchestration;

public sealed record EnqueueIssuesRequest(
    int? MappingId,
    SonarIssueType IssueType,
    string? Instructions,
    IReadOnlyList<SonarIssueTransport>? Issues);
