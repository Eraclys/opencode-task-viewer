namespace TaskViewer.Server.Infrastructure.OpenCode;

sealed class OpenCodeViewerUpdateNotifier
{
    readonly OpenCodeViewerCacheCoordinator _cacheCoordinator;
    readonly SseHub _sseHub;

    public OpenCodeViewerUpdateNotifier(OpenCodeViewerCacheCoordinator cacheCoordinator, SseHub sseHub)
    {
        _cacheCoordinator = cacheCoordinator;
        _sseHub = sseHub;
    }

    public void InvalidateAllCaches() => _cacheCoordinator.InvalidateAllCaches();

    public Task BroadcastUpdateAsync() => _sseHub.Broadcast(
        new ViewerUpdateEventDto
        {
            Type = "update"
        });

    public Task InvalidateAllAndBroadcastAsync()
    {
        _cacheCoordinator.InvalidateAllCaches();
        return BroadcastUpdateAsync();
    }
}
