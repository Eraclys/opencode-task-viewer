using OpenCode.Client;
using SonarQube.OpenCodeTaskViewer.Domain;

namespace SonarQube.OpenCodeTaskViewer.Infrastructure.OpenCode;

public static class OpenCodeEventParser
{
    public static OpenCodeEventEnvelope? Parse(OpenCodeSseEvent? evt)
    {
        var directoryRaw = evt?.Directory;
        var directory = DirectoryPath.Normalize(directoryRaw) ?? directoryRaw;
        var type = evt?.Payload?.Type?.Trim();

        if (string.IsNullOrWhiteSpace(type))
            return null;

        var properties = evt?.Payload?.Properties;
        var sessionId = ReadSessionId(properties);
        var statusType = ReadStatusType(properties);

        return new OpenCodeEventEnvelope(
            directory,
            type,
            sessionId,
            statusType);
    }

    static string? ReadSessionId(OpenCodeSseProperties? properties)
        => properties?.LegacySessionId ?? properties?.SessionId;

    static SessionRuntimeStatus? ReadStatusType(OpenCodeSseProperties? properties)
    {
        var raw = properties?.Status?.Type ?? properties?.Type;

        return string.IsNullOrWhiteSpace(raw) ? null : SessionRuntimeStatus.FromRaw(raw);
    }
}
