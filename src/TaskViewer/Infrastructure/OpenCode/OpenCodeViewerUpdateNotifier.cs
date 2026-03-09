namespace TaskViewer.Infrastructure.OpenCode;

public sealed class OpenCodeViewerUpdateNotifier
{
    readonly OpenCodeViewerCacheCoordinator _cacheCoordinator;
    readonly ISseHub _sseHub;

    public OpenCodeViewerUpdateNotifier(OpenCodeViewerCacheCoordinator cacheCoordinator, ISseHub sseHub)
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
