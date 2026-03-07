using System.Collections.Concurrent;
using TaskViewer.Server.Application.Orchestration;
using TaskViewer.SonarQube;

namespace TaskViewer.Server.Infrastructure.Orchestration;

public sealed class CachedSonarRuleReadService : ISonarRuleReadService
{
    readonly ConcurrentDictionary<string, string> _cache = new(StringComparer.OrdinalIgnoreCase);
    readonly ISonarQubeService _sonarQubeService;

    public CachedSonarRuleReadService(ISonarQubeService sonarQubeService)
    {
        _sonarQubeService = sonarQubeService;
    }

    public async Task<string> GetRuleDisplayName(string ruleKey)
    {
        var normalizedKey = (ruleKey ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(normalizedKey))
            return string.Empty;

        if (_cache.TryGetValue(normalizedKey, out var cached))
            return cached;

        try
        {
            var rule = await _sonarQubeService.GetRuleAsync(normalizedKey);
            var name = rule.Name;

            if (string.IsNullOrWhiteSpace(name))
                name = normalizedKey;

            _cache[normalizedKey] = name;

            return name;
        }
        catch
        {
            _cache[normalizedKey] = normalizedKey;

            return normalizedKey;
        }
    }
}
