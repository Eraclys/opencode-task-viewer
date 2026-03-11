using SonarQube.Client;
using SonarQube.OpenCodeTaskViewer.Infrastructure.Orchestration;
using SonarQube.OpenCodeTaskViewer.Infrastructure.Persistence;

namespace SonarQube.OpenCodeTaskViewer.Domain.Orchestration;

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

    public Task<List<MappingRecord>> ListMappingsAsync(CancellationToken cancellationToken = default) => _mappingRepository.ListMappings(cancellationToken);

    public async Task<MappingRecord?> GetMappingByIdAsync(int? mappingId, CancellationToken cancellationToken = default)
    {
        var id = mappingId.GetValueOrDefault(-1);

        if (id <= 0)
            return null;

        return await _mappingRepository.GetMappingById(id, cancellationToken);
    }

    public async Task<bool> DeleteMappingAsync(int? mappingId, CancellationToken cancellationToken = default)
    {
        var id = mappingId.GetValueOrDefault(-1);

        if (id <= 0)
            return false;

        return await _mappingRepository.DeleteMapping(id, cancellationToken);
    }

    public async Task<MappingRecord> UpsertMappingAsync(UpsertMappingRequest request, CancellationToken cancellationToken = default)
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
            _nowUtc(),
            cancellationToken);
    }

    public async Task<InstructionProfileRecord?> GetInstructionProfileAsync(int? mappingId, SonarIssueType issueType, CancellationToken cancellationToken = default)
    {
        var mapping = await GetMappingByIdAsync(mappingId, cancellationToken);

        if (mapping is null)
            return null;

        if (!issueType.HasValue)
            return null;

        return await _mappingRepository.GetInstructionProfile(mapping.Id, issueType, cancellationToken);
    }

    public async Task<InstructionProfileRecord> UpsertInstructionProfileAsync(UpsertInstructionProfileRequest request, CancellationToken cancellationToken = default)
    {
        var mapping = await GetMappingByIdAsync(request.MappingId, cancellationToken);

        if (mapping is null)
            throw new InvalidOperationException("Mapping not found");

        var type = request.IssueType;

        if (!type.HasValue)
            throw new InvalidOperationException("Missing issueType");

        var text = (request.Instructions ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException("Missing instructions");

        return await _mappingRepository.UpsertInstructionProfile(
            mapping.Id,
            type,
            text,
            _nowUtc(),
            cancellationToken);
    }
}
