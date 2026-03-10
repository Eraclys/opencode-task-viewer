using TaskViewer.Infrastructure.ServerSentEvents;
using TaskViewer.OpenCode;

namespace TaskViewer.Infrastructure.OpenCode;

public sealed class OpenCodeEventHandler
{
    readonly OpenCodeCacheInvalidationPolicy _cacheInvalidationPolicy;
    readonly OpenCodeViewerCachePolicy _cachePolicy;
    readonly OpenCodeViewerState _viewerState;
    readonly ISseHub _sseHub;

    public OpenCodeEventHandler(OpenCodeViewerState viewerState, ISseHub sseHub, OpenCodeCacheInvalidationPolicy cacheInvalidationPolicy, OpenCodeViewerCachePolicy cachePolicy)
    {
        _cacheInvalidationPolicy = cacheInvalidationPolicy;
        _cachePolicy = cachePolicy;
        _viewerState = viewerState;
        _sseHub = sseHub;
    }

    public async Task HandleAsync(OpenCodeSseEvent evt)
    {
        var parsed = OpenCodeEventParser.Parse(evt);

        if (parsed is null)
            return;

        var decision = _cacheInvalidationPolicy.Decide(parsed);

        if (decision == OpenCodeCacheInvalidationDecision.None)
            return;

        if (decision.InvalidateSessionTodos && !string.IsNullOrWhiteSpace(parsed.SessionId))
            _viewerState.InvalidateTodos(parsed.Directory, parsed.SessionId);

        if (!string.IsNullOrWhiteSpace(parsed.SessionId) && decision.StatusType is { } statusType)
            _viewerState.NoteStatusOverride(decision.StatusDirectory, parsed.SessionId, statusType, _cachePolicy.StatusOverrideTtlMs);

        if (decision.ClearAssistantPresence)
            _viewerState.ClearAssistantPresence();

        if (decision.InvalidateAllCaches)
            _viewerState.InvalidateAllCaches();

        if (decision.InvalidateSessionsList)
            _viewerState.InvalidateSessionsList();

        if (decision.InvalidateTaskOverview)
            _viewerState.InvalidateTaskOverview();

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
