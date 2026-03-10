using SonarQube.Client;

namespace SonarQube.OpenCodeTaskViewer.Domain.Orchestration;

public interface IEnqueueContextResolver
{
    Task<EnqueueContext> ResolveAsync(
        int? mappingId,
        SonarIssueType issueType,
        string? instructions,
        CancellationToken cancellationToken = default);
}
