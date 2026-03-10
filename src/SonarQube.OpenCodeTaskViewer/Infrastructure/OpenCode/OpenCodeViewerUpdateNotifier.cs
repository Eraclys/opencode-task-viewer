using SonarQube.OpenCodeTaskViewer.Infrastructure.ServerSentEvents;

namespace SonarQube.OpenCodeTaskViewer.Infrastructure.OpenCode;

public sealed class OpenCodeViewerUpdateNotifier
{
    readonly ISseHub _sseHub;
    readonly OpenCodeViewerState _viewerState;

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
