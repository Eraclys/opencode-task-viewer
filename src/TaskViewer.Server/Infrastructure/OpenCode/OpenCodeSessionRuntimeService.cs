using TaskViewer.Server.Application.Sessions;

namespace TaskViewer.Server.Infrastructure.OpenCode;

sealed class OpenCodeSessionRuntimeService
{
    readonly OpenCodeViewerCacheCoordinator _cacheCoordinator;

    public OpenCodeSessionRuntimeService(OpenCodeViewerCacheCoordinator cacheCoordinator)
    {
        _cacheCoordinator = cacheCoordinator;
    }

    public string NormalizeRuntimeStatus(string? directory, string sessionId, Dictionary<string, SessionRuntimeStatus> statusMap)
    {
        if (_cacheCoordinator.TryGetRecentStatusOverride(directory, sessionId, out var overrideType))
            return string.IsNullOrWhiteSpace(overrideType) ? "idle" : overrideType;

        if (statusMap.TryGetValue(sessionId, out var status) &&
            !string.IsNullOrWhiteSpace(status.Type))
            return status.Type;

        return "idle";
    }
}
