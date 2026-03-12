namespace SonarQube.OpenCodeTaskViewer.MockOpenCode;

sealed class AddSandboxSessionRequest
{
    public string? ProjectWorktree { get; init; }
    public string? Worktree { get; init; }
    public string? SandboxPath { get; init; }
    public string? Sandbox { get; init; }
    public string? SessionId { get; init; }
    public string? Title { get; init; }
    public string? Directory { get; init; }
}