namespace TaskViewer.OpenCode;

public sealed record OpenCodeMessage(string Role, string Text, DateTimeOffset? CreatedAt);
