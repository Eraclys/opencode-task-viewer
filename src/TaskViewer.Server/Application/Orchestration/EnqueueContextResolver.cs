using TaskViewer.Server.Infrastructure.Orchestration;

namespace TaskViewer.Server.Application.Orchestration;

public sealed class EnqueueContextResolver : IEnqueueContextResolver
{
    readonly IMappingRepository _mappingRepository;

    public EnqueueContextResolver(IMappingRepository mappingRepository)
    {
        _mappingRepository = mappingRepository;
    }

    public async Task<EnqueueContext> ResolveAsync(object? mappingId, string? issueType, string? instructions)
    {
        var id = ParseIntSafe(mappingId, -1);

        if (id <= 0)
            throw new InvalidOperationException("Mapping not found or disabled");

        var mapping = await _mappingRepository.GetMappingById(id);

        if (mapping is null ||
            !mapping.Enabled)
            throw new InvalidOperationException("Mapping not found or disabled");

        var type = NormalizeIssueType(issueType);
        var profile = type is null ? null : await _mappingRepository.GetInstructionProfile(mapping.Id, type);
        var defaultInstruction = profile?["instructions"]?.ToString();
        var instructionText = EnqueueContextPolicy.ResolveInstructionText(instructions, defaultInstruction);

        if (EnqueueContextPolicy.ShouldPersistInstructionProfile(type, instructionText))
            await _mappingRepository.UpsertInstructionProfile(
                mapping.Id,
                type!,
                instructionText,
                NowIso());

        return new EnqueueContext(mapping, type, instructionText);
    }

    static string NowIso() => DateTimeOffset.UtcNow.ToString("O");

    static int ParseIntSafe(object? value, int fallback)
    {
        if (value is null)
            return fallback;

        if (value is int i)
            return i;

        if (value is long l &&
            l is >= int.MinValue and <= int.MaxValue)
            return (int)l;

        return int.TryParse(value.ToString(), out var parsed) ? parsed : fallback;
    }

    static string? NormalizeIssueType(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().ToUpperInvariant();

        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }
}
