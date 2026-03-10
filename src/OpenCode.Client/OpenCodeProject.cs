namespace OpenCode.Client;

public sealed record OpenCodeProject(
    string? Worktree,
    IReadOnlyList<string> SandboxDirectories);
