namespace TaskViewer.Infrastructure.OpenCode;

public sealed record OpenCodeEventEnvelope(string? Directory, string Type, string? SessionId, string? StatusType);
