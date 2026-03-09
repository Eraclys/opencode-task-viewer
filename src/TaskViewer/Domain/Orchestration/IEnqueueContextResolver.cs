namespace TaskViewer.Domain.Orchestration;

public interface IEnqueueContextResolver
{
    Task<EnqueueContext> ResolveAsync(int? mappingId, string? issueType, string? instructions, CancellationToken cancellationToken = default);
}
