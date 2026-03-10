using TaskViewer.SonarQube;

namespace TaskViewer.Domain.Orchestration;

public interface IEnqueueContextResolver
{
    Task<EnqueueContext> ResolveAsync(int? mappingId, SonarIssueType issueType, string? instructions, CancellationToken cancellationToken = default);
}
