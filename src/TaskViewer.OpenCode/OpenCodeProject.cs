namespace TaskViewer.OpenCode;

public sealed record OpenCodeProject(
    string? Worktree,
    IReadOnlyList<string> SandboxDirectories);
