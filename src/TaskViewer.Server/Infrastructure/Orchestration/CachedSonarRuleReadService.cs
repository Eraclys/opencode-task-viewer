using System.Collections.Concurrent;
using TaskViewer.Server.Application.Orchestration;

namespace TaskViewer.Server.Infrastructure.Orchestration;

public sealed class CachedSonarRuleReadService : ISonarRuleReadService
{
    private readonly ISonarGateway _sonarGateway;
    private readonly ConcurrentDictionary<string, string> _cache = new(StringComparer.OrdinalIgnoreCase);

    public CachedSonarRuleReadService(ISonarGateway sonarGateway)
    {
        _sonarGateway = sonarGateway;
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
            var data = await _sonarGateway.Fetch(
                "/api/rules/show",
                new Dictionary<string, string?>
                {
                    ["key"] = normalizedKey
                });

            var name = data?["rule"]?["name"]?.ToString()?.Trim();
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
