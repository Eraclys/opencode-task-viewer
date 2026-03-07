namespace TaskViewer.Server.Infrastructure.Orchestration;

public sealed class InstructionProfileRecord
{
    public required int Id { get; init; }
    public required int MappingId { get; init; }
    public required string IssueType { get; init; }
    public required string Instructions { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
}
