using TaskViewer.Server;

namespace TaskViewer.Server.Application.Orchestration;

public interface IEnqueueContextResolver
{
    Task<EnqueueContext> ResolveAsync(object? mappingId, string? issueType, string? instructions);
}

public sealed record EnqueueContext(
    MappingRecord Mapping,
    string? Type,
    string InstructionText);
