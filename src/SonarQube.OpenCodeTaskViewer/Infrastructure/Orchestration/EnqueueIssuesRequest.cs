using SonarQube.Client;

namespace SonarQube.OpenCodeTaskViewer.Infrastructure.Orchestration;

public sealed record EnqueueIssuesRequest(
    int? MappingId,
    SonarIssueType IssueType,
    string? Instructions,
    IReadOnlyList<SonarIssueTransport>? Issues);
