using TaskViewer.Infrastructure.Orchestration;

namespace TaskViewer.Application.Orchestration;

sealed class OrchestrationMappingService : IOrchestrationMappingService
{
    readonly IMappingRepository _mappingRepository;
    readonly Func<string?, string?> _normalizeDirectory;
    readonly Func<DateTimeOffset> _nowUtc;

    public OrchestrationMappingService(
        IMappingRepository mappingRepository,
        Func<string?, string?> normalizeDirectory,
        Func<DateTimeOffset>? nowUtc = null)
    {
        _mappingRepository = mappingRepository;
        _normalizeDirectory = normalizeDirectory;
        _nowUtc = nowUtc ?? (() => DateTimeOffset.UtcNow);
    }

    public Task<List<MappingRecord>> ListMappingsAsync() => _mappingRepository.ListMappings();

    public async Task<MappingRecord?> GetMappingByIdAsync(int? mappingId)
    {
        var id = mappingId.GetValueOrDefault(-1);

        if (id <= 0)
            return null;

        return await _mappingRepository.GetMappingById(id);
    }

    public async Task<bool> DeleteMappingAsync(int? mappingId)
    {
        var id = mappingId.GetValueOrDefault(-1);

        if (id <= 0)
            return false;

        return await _mappingRepository.DeleteMapping(id);
    }

    public async Task<MappingRecord> UpsertMappingAsync(UpsertMappingRequest request)
    {
        var sonarProjectKey = request.SonarProjectKey ?? string.Empty;
        var directory = request.Directory ?? string.Empty;
        var branch = request.Branch;
        var enabled = request.Enabled;

        if (string.IsNullOrWhiteSpace(sonarProjectKey))
            throw new InvalidOperationException("Missing sonarProjectKey");

        if (string.IsNullOrWhiteSpace(directory))
            throw new InvalidOperationException("Missing directory");

        directory = _normalizeDirectory(directory) ?? directory.Replace('\\', '/');
        var id = request.Id.GetValueOrDefault(-1);

        return await _mappingRepository.UpsertMapping(
            id > 0 ? id : null,
            sonarProjectKey,
            directory,
            branch,
            enabled,
            _nowUtc());
    }

    public async Task<InstructionProfileRecord?> GetInstructionProfileAsync(int? mappingId, string? issueType)
    {
        var mapping = await GetMappingByIdAsync(mappingId);

        if (mapping is null)
            return null;

        var type = NormalizeIssueType(issueType);

        if (type is null)
            return null;

        return await _mappingRepository.GetInstructionProfile(mapping.Id, type);
    }

    public async Task<InstructionProfileRecord> UpsertInstructionProfileAsync(UpsertInstructionProfileRequest request)
    {
        var mapping = await GetMappingByIdAsync(request.MappingId);

        if (mapping is null)
            throw new InvalidOperationException("Mapping not found");

        var type = NormalizeIssueType(request.IssueType);

        if (type is null)
            throw new InvalidOperationException("Missing issueType");

        var text = (request.Instructions ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException("Missing instructions");

        return await _mappingRepository.UpsertInstructionProfile(
            mapping.Id,
            type,
            text,
            _nowUtc());
    }

    static string? NormalizeIssueType(string? value)
    {
        var v = (value ?? string.Empty).Trim().ToUpperInvariant();

        return string.IsNullOrWhiteSpace(v) ? null : v;
    }
}
