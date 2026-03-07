namespace TaskViewer.Server.Application.Orchestration;

public sealed record SonarIssueSummaryItem(
    string Key,
    string Type,
    string? Severity,
    string? Rule,
    string? Message,
    string? Component,
    int? Line,
    string? Status,
    string? RelativePath,
    string? AbsolutePath);
