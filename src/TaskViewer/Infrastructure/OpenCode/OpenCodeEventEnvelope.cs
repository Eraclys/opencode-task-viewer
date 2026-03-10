using TaskViewer.Domain.Sessions;

namespace TaskViewer.Infrastructure.OpenCode;

public sealed record OpenCodeEventEnvelope(string? Directory, string Type, string? SessionId, SessionRuntimeStatus? StatusType);
