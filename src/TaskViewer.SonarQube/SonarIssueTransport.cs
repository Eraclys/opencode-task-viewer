namespace TaskViewer.SonarQube;

public sealed record SonarIssueTransport(
    string? Key,
    string? IssueKey,
    string? Type,
    string? IssueType,
    string? Severity,
    string? Rule,
    string? Message,
    string? Component,
    string? File,
    string? Line,
    string? Status);
