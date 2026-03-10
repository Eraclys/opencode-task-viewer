namespace SonarQube.OpenCodeTaskViewer.Infrastructure.OpenCode;

public sealed class OpenCodeCacheInvalidationPolicy
{
    public OpenCodeCacheInvalidationDecision Decide(OpenCodeEventEnvelope parsed)
    {
        if (parsed.Type == "todo.updated")
        {
            if (!string.IsNullOrWhiteSpace(parsed.SessionId))
                return new OpenCodeCacheInvalidationDecision(
                    true,
                    false,
                    false,
                    false,
                    true,
                    parsed.SessionId);

            return new OpenCodeCacheInvalidationDecision(
                false,
                true,
                false,
                false,
                true,
                null);
        }

        if (parsed.Type == "session.status")
        {
            if (!string.IsNullOrWhiteSpace(parsed.SessionId) &&
                parsed.StatusType is { } statusType)
                return new OpenCodeCacheInvalidationDecision(
                    false,
                    false,
                    true,
                    true,
                    true,
                    parsed.SessionId,
                    parsed.Directory,
                    statusType);

            return new OpenCodeCacheInvalidationDecision(
                false,
                false,
                false,
                false,
                true,
                null);
        }

        if (parsed.Type is "session.created" or "session.updated" or "session.deleted")
            return new OpenCodeCacheInvalidationDecision(
                false,
                true,
                false,
                false,
                true,
                null);

        if (parsed.Type.StartsWith("message.", StringComparison.Ordinal))
            return new OpenCodeCacheInvalidationDecision(
                false,
                false,
                true,
                false,
                true,
                null,
                ClearAssistantPresence: true);

        return OpenCodeCacheInvalidationDecision.None;
    }
}
