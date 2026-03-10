using SonarQube.Client;
using SonarQube.OpenCodeTaskViewer.Infrastructure.Persistence;

namespace SonarQube.OpenCodeTaskViewer.Domain.Orchestration;

public sealed class EnqueueContextResolver : IEnqueueContextResolver
{
    readonly IMappingRepository _mappingRepository;

    public EnqueueContextResolver(IMappingRepository mappingRepository)
    {
        _mappingRepository = mappingRepository;
    }

    public async Task<EnqueueContext> ResolveAsync(
        int? mappingId,
        SonarIssueType issueType,
        string? instructions,
        CancellationToken cancellationToken = default)
    {
        var id = mappingId.GetValueOrDefault(-1);

        if (id <= 0)
            throw new InvalidOperationException("Mapping not found or disabled");

        var mapping = await _mappingRepository.GetMappingById(id, cancellationToken);

        if (mapping is null ||
            !mapping.Enabled)
            throw new InvalidOperationException("Mapping not found or disabled");

        var normalizedType = issueType.OrNull();
        var profile = normalizedType is null ? null : await _mappingRepository.GetInstructionProfile(mapping.Id, normalizedType, cancellationToken);
        var defaultInstruction = profile?.Instructions;
        var instructionText = EnqueueContextPolicy.ResolveInstructionText(instructions, defaultInstruction);

        if (EnqueueContextPolicy.ShouldPersistInstructionProfile(normalizedType, instructionText))
        {
            await _mappingRepository.UpsertInstructionProfile(
                mapping.Id,
                normalizedType!,
                instructionText,
                DateTimeOffset.UtcNow,
                cancellationToken);
        }

        return new EnqueueContext(mapping, issueType, instructionText);
    }
}
