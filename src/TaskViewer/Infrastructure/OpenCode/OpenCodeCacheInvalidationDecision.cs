using TaskViewer.Domain.Sessions;

namespace TaskViewer.Infrastructure.OpenCode;

public sealed record OpenCodeCacheInvalidationDecision(
    bool InvalidateSessionTodos,
    bool InvalidateAllCaches,
    bool InvalidateSessionsList,
    bool InvalidateTaskOverview,
    bool BroadcastUpdate,
    string? BroadcastSessionId,
    string? StatusDirectory = null,
    SessionRuntimeStatus? StatusType = null,
    bool ClearAssistantPresence = false)
{
    public static OpenCodeCacheInvalidationDecision None { get; } = new(false, false, false, false, false, null);
}
