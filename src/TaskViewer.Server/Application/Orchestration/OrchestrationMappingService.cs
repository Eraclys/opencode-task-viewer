using System.Globalization;
using System.Text.Json.Nodes;
using TaskViewer.Server;
using TaskViewer.Server.Infrastructure.Orchestration;

namespace TaskViewer.Server.Application.Orchestration;

internal sealed class OrchestrationMappingService : IOrchestrationMappingService
{
    private readonly IMappingRepository _mappingRepository;
    private readonly Func<string?, string?> _normalizeDirectory;
    private readonly Func<string> _nowIso;

    public OrchestrationMappingService(
        IMappingRepository mappingRepository,
        Func<string?, string?> normalizeDirectory,
        Func<string>? nowIso = null)
    {
        _mappingRepository = mappingRepository;
        _normalizeDirectory = normalizeDirectory;
        _nowIso = nowIso ?? (() => DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
    }

    public Task<List<MappingRecord>> ListMappingsAsync()
    {
        return _mappingRepository.ListMappings();
    }

    public async Task<MappingRecord?> GetMappingByIdAsync(object? mappingId)
    {
        var id = ParseIntSafe(mappingId, -1);

        if (id <= 0)
            return null;

        return await _mappingRepository.GetMappingById(id);
    }

    public async Task<MappingRecord> UpsertMappingAsync(JsonNode? payload)
    {
        var sonarProjectKey = payload?["sonarProjectKey"]?.ToString()?.Trim()
                              ?? payload?["sonar_project_key"]?.ToString()?.Trim()
                              ?? string.Empty;
        var directory = payload?["directory"]?.ToString()?.Trim() ?? string.Empty;
        var branch = payload?["branch"]?.ToString()?.Trim();

        if (string.IsNullOrWhiteSpace(branch))
            branch = null;

        var enabled = payload?["enabled"] is null || payload?["enabled"]?.GetValue<bool>() != false;

        if (string.IsNullOrWhiteSpace(sonarProjectKey))
            throw new InvalidOperationException("Missing sonarProjectKey");

        if (string.IsNullOrWhiteSpace(directory))
            throw new InvalidOperationException("Missing directory");

        directory = _normalizeDirectory(directory) ?? directory.Replace('\\', '/');
        var id = ParseIntSafe(payload?["id"]?.ToString(), -1);

        return await _mappingRepository.UpsertMapping(
            id > 0 ? id : null,
            sonarProjectKey,
            directory,
            branch,
            enabled,
            _nowIso());
    }

    public async Task<JsonObject?> GetInstructionProfileAsync(object? mappingId, string? issueType)
    {
        var mapping = await GetMappingByIdAsync(mappingId);

        if (mapping is null)
            return null;

        var type = NormalizeIssueType(issueType);

        if (type is null)
            return null;

        return await _mappingRepository.GetInstructionProfile(mapping.Id, type);
    }

    public async Task<JsonObject> UpsertInstructionProfileAsync(object? mappingId, string? issueType, string? instructions)
    {
        var mapping = await GetMappingByIdAsync(mappingId);

        if (mapping is null)
            throw new InvalidOperationException("Mapping not found");

        var type = NormalizeIssueType(issueType);

        if (type is null)
            throw new InvalidOperationException("Missing issueType");

        var text = (instructions ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException("Missing instructions");

        return await _mappingRepository.UpsertInstructionProfile(mapping.Id, type, text, _nowIso());
    }

    private static int ParseIntSafe(object? value, int fallback)
    {
        if (value is null)
            return fallback;

        if (value is int i)
            return i;

        if (value is long l && l is >= int.MinValue and <= int.MaxValue)
            return (int)l;

        var s = Convert.ToString(value, CultureInfo.InvariantCulture);

        return int.TryParse(
            s,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var n)
            ? n
            : fallback;
    }

    private static string? NormalizeIssueType(string? value)
    {
        var v = (value ?? string.Empty).Trim().ToUpperInvariant();
        return string.IsNullOrWhiteSpace(v) ? null : v;
    }
}
