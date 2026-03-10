namespace SonarQube.OpenCodeTaskViewer.Domain.Orchestration;

public sealed class RuleCountDto
{
    public required string Key { get; init; }
    public required string Name { get; init; }
    public required int Count { get; init; }
}
