namespace TaskViewer.OpenCode;

public sealed record OpenCodeMessageTransport(string Role, string Text, DateTimeOffset? CreatedAt);
