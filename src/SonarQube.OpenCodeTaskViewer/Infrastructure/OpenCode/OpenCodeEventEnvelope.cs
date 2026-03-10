using OpenCode.Client;

namespace SonarQube.OpenCodeTaskViewer.Infrastructure.OpenCode;

public sealed record OpenCodeEventEnvelope(
    string? Directory,
    string Type,
    string? SessionId,
    SessionRuntimeStatus? StatusType);
