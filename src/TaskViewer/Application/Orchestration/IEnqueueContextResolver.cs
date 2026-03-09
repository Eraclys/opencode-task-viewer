namespace TaskViewer.Application.Orchestration;

public interface IEnqueueContextResolver
{
    Task<EnqueueContext> ResolveAsync(int? mappingId, string? issueType, string? instructions);
}
