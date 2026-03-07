namespace TaskViewer.OpenCode;

public sealed record OpenCodeProjectTransport(
    string? Worktree,
    IReadOnlyList<string> SandboxDirectories);
