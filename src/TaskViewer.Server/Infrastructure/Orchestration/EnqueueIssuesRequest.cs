using TaskViewer.SonarQube;

namespace TaskViewer.Server.Infrastructure.Orchestration;

public sealed record EnqueueIssuesRequest(
    int? MappingId,
    string? IssueType,
    string? Instructions,
    IReadOnlyList<SonarIssueTransport>? Issues);
