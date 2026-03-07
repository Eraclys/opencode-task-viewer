namespace TaskViewer.Server.Infrastructure.OpenCode;

sealed record OpenCodeEventEnvelope(string? Directory, string Type, string? SessionId, string? StatusType);
