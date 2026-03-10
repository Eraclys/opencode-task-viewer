using TaskViewer.Domain.Sessions;
using TaskViewer.Infrastructure.Persistence;
using TaskViewer.OpenCode;

namespace TaskViewer.Domain.Orchestration;

public sealed class WorkingSessionsReadService : IWorkingSessionsReadService
{
    readonly IMappingRepository _mappingRepository;
    readonly IOpenCodeService _openCodeService;
    (DateTimeOffset Ts, int Count) _cachedSample = (DateTimeOffset.MinValue, 0);

    public WorkingSessionsReadService(
        IMappingRepository mappingRepository,
        IOpenCodeService openCodeService)
    {
        _mappingRepository = mappingRepository;
        _openCodeService = openCodeService;
    }

    public async Task<WorkingSessionsSample> GetWorkingSessionsCountAsync(bool forceRefresh, int pollMs)
    {
        var now = DateTimeOffset.UtcNow;
        var cacheTtlMs = Math.Clamp(pollMs, 500, 5000);

        if (!forceRefresh &&
            (now - _cachedSample.Ts).TotalMilliseconds < cacheTtlMs)
            return new WorkingSessionsSample(_cachedSample.Ts, _cachedSample.Count);

        var dirs = await _mappingRepository.ListEnabledMappingDirectories();
        var totalRunning = 0;

        foreach (var dir in dirs)
        {
            var map = await FetchStatusMapForDirectory(dir);
            totalRunning += map.Values.Count(status => status.IsRunning);
        }

        _cachedSample = (now, totalRunning);

        return new WorkingSessionsSample(now, totalRunning);
    }

    async Task<Dictionary<string, SessionRuntimeStatus>> FetchStatusMapForDirectory(string directory)
    {
        var variants = GetDirectoryVariants(directory);

        if (variants.Count == 0)
            return new Dictionary<string, SessionRuntimeStatus>(StringComparer.Ordinal);

        Dictionary<string, SessionRuntimeStatus> fallback = new(StringComparer.Ordinal);

        foreach (var variant in variants)
        {
            try
            {
                var map = await _openCodeService.ReadWorkingStatusMapAsync(variant);

                if (map.Count > 0)
                    return map;

                if (fallback.Count == 0)
                    fallback = map;
            }
            catch
            {
            }
        }

        return fallback;
    }

    static List<string> GetDirectoryVariants(string? directory)
        => DirectoryPath.GetVariants(directory);
}
