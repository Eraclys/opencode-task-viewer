using TaskViewer.SonarQube;

namespace TaskViewer.Infrastructure.Orchestration;

public sealed record EnqueueIssuesRequest(
    int? MappingId,
    string? IssueType,
    string? Instructions,
    IReadOnlyList<SonarIssueTransport>? Issues);
