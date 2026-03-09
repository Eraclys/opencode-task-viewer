using System.Text.Json.Nodes;

namespace TaskViewer.Infrastructure.OpenCode;

public sealed class OpenCodeEventHandler
{
    readonly OpenCodeCacheInvalidationPolicy _cacheInvalidationPolicy;
    readonly OpenCodeViewerCacheCoordinator _cacheCoordinator;
    readonly ISseHub _sseHub;

    public OpenCodeEventHandler(OpenCodeViewerCacheCoordinator cacheCoordinator, ISseHub sseHub, OpenCodeCacheInvalidationPolicy cacheInvalidationPolicy)
    {
        _cacheInvalidationPolicy = cacheInvalidationPolicy;
        _cacheCoordinator = cacheCoordinator;
        _sseHub = sseHub;
    }

    public async Task HandleAsync(JsonNode evt)
    {
        var parsed = OpenCodeEventParser.Parse(evt);

        if (parsed is null)
            return;

        var decision = _cacheInvalidationPolicy.Decide(parsed);

        if (decision == OpenCodeCacheInvalidationDecision.None)
            return;

        if (decision.InvalidateSessionTodos && !string.IsNullOrWhiteSpace(parsed.SessionId))
            _cacheCoordinator.InvalidateTodos(parsed.Directory, parsed.SessionId);

        if (!string.IsNullOrWhiteSpace(parsed.SessionId) && !string.IsNullOrWhiteSpace(decision.StatusType))
            _cacheCoordinator.NoteStatusOverride(decision.StatusDirectory, parsed.SessionId, decision.StatusType);

        if (decision.ClearAssistantPresence)
            _cacheCoordinator.ClearAssistantPresence();

        if (decision.InvalidateAllCaches)
            _cacheCoordinator.InvalidateAllCaches();

        if (decision.InvalidateSessionsList)
            _cacheCoordinator.InvalidateSessionsList();

        if (decision.InvalidateTaskOverview)
            _cacheCoordinator.InvalidateTaskOverview();

        if (decision.BroadcastUpdate)
            await BroadcastUpdate(decision.BroadcastSessionId);
    }

    Task BroadcastUpdate(string? sessionId = null)
        => string.IsNullOrWhiteSpace(sessionId)
            ? _sseHub.Broadcast(
                new ViewerUpdateEventDto
                {
                    Type = "update"
                })
            : _sseHub.Broadcast(
                new ViewerUpdateEventDto
                {
                    Type = "update",
                    SessionId = sessionId
                });
}
