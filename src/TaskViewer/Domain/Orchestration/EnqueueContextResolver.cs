using TaskViewer.Infrastructure.Persistence;

namespace TaskViewer.Domain.Orchestration;

public sealed class EnqueueContextResolver : IEnqueueContextResolver
{
    readonly IMappingRepository _mappingRepository;

    public EnqueueContextResolver(IMappingRepository mappingRepository)
    {
        _mappingRepository = mappingRepository;
    }

    public async Task<EnqueueContext> ResolveAsync(int? mappingId, string? issueType, string? instructions, CancellationToken cancellationToken = default)
    {
        var id = mappingId.GetValueOrDefault(-1);

        if (id <= 0)
            throw new InvalidOperationException("Mapping not found or disabled");

        var mapping = await _mappingRepository.GetMappingById(id, cancellationToken);

        if (mapping is null ||
            !mapping.Enabled)
            throw new InvalidOperationException("Mapping not found or disabled");

        var type = NormalizeIssueType(issueType);
        var profile = type is null ? null : await _mappingRepository.GetInstructionProfile(mapping.Id, type, cancellationToken);
        var defaultInstruction = profile?.Instructions;
        var instructionText = EnqueueContextPolicy.ResolveInstructionText(instructions, defaultInstruction);

        if (EnqueueContextPolicy.ShouldPersistInstructionProfile(type, instructionText))
            await _mappingRepository.UpsertInstructionProfile(
                mapping.Id,
                type!,
                instructionText,
                DateTimeOffset.UtcNow,
                cancellationToken);

        return new EnqueueContext(mapping, type, instructionText);
    }

    static string? NormalizeIssueType(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().ToUpperInvariant();

        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }
}
