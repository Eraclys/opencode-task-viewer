using System.Text.Json.Nodes;

namespace TaskViewer.Server;

public sealed class SonarOrchestratorOptions
{
    public required string SonarUrl { get; init; }
    public required string SonarToken { get; init; }
    public required string DbPath { get; init; }
    public required int MaxActive { get; init; }
    public required int PollMs { get; init; }
    public required int MaxAttempts { get; init; }
    public required int MaxWorkingGlobal { get; init; }
    public required int WorkingResumeBelow { get; init; }
    public required Func<string, OpenCodeRequest, Task<JsonNode?>> OpenCodeFetch { get; init; }
    public required Func<string?, string?> NormalizeDirectory { get; init; }
    public required Func<string, string?, string?> BuildOpenCodeSessionUrl { get; init; }
    public required Action OnChange { get; init; }
}