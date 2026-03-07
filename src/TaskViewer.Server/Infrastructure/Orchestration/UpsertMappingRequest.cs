namespace TaskViewer.Server.Infrastructure.Orchestration;

public sealed record UpsertMappingRequest(
    int? Id,
    string? SonarProjectKey,
    string? Directory,
    string? Branch,
    bool Enabled);
