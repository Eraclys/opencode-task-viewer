using TaskViewer.Infrastructure.Orchestration;
using TaskViewer.OpenCode;

namespace TaskViewer.Application.Orchestration;

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
            totalRunning += map.Values.Count(IsRunningStatusType);
        }

        _cachedSample = (now, totalRunning);

        return new WorkingSessionsSample(now, totalRunning);
    }

    async Task<Dictionary<string, string>> FetchStatusMapForDirectory(string directory)
    {
        var variants = GetDirectoryVariants(directory);

        if (variants.Count == 0)
            return new Dictionary<string, string>(StringComparer.Ordinal);

        Dictionary<string, string> fallback = new(StringComparer.Ordinal);

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
    {
        var dir = (directory ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(dir))
            return [];

        if (dir.Length > 1 &&
            (dir.EndsWith('/') || dir.EndsWith('\\')))
            dir = dir.TrimEnd('/', '\\');

        var variants = new List<string>
        {
            dir
        };

        var forward = dir.Replace('\\', '/');
        var backward = dir.Replace('/', '\\');

        if (!variants.Contains(forward, StringComparer.Ordinal))
            variants.Add(forward);

        if (!variants.Contains(backward, StringComparer.Ordinal))
            variants.Add(backward);

        return variants;
    }

    static bool IsRunningStatusType(string? value)
    {
        var t = (value ?? string.Empty).Trim().ToLowerInvariant();

        return t is "busy" or "retry" or "running";
    }

}
