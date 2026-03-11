using SonarQube.Client;

namespace SonarQube.OpenCodeTaskViewer.Infrastructure.Persistence;

public sealed class InstructionProfileRecord
{
    public required int Id { get; init; }
    public required int MappingId { get; init; }
    public required string IssueType { get; init; }
    public SonarIssueType IssueTypeValue => SonarIssueType.FromRaw(IssueType);
    public required string Instructions { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
}
