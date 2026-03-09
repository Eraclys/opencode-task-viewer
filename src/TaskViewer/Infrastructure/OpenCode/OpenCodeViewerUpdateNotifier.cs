using TaskViewer.Infrastructure.ServerSentEvents;

namespace TaskViewer.Infrastructure.OpenCode;

public sealed class OpenCodeViewerUpdateNotifier
{
    readonly OpenCodeViewerState _viewerState;
    readonly ISseHub _sseHub;

    public OpenCodeViewerUpdateNotifier(OpenCodeViewerState viewerState, ISseHub sseHub)
    {
        _viewerState = viewerState;
        _sseHub = sseHub;
    }

    public void InvalidateAllCaches() => _viewerState.InvalidateAllCaches();

    public Task BroadcastUpdateAsync() => _sseHub.Broadcast(
        new ViewerUpdateEventDto
        {
            Type = "update"
        });

    public Task InvalidateAllAndBroadcastAsync()
    {
        _viewerState.InvalidateAllCaches();
        return BroadcastUpdateAsync();
    }
}
