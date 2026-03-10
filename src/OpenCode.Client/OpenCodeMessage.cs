namespace OpenCode.Client;

public sealed record OpenCodeMessage(string Role, string Text, DateTimeOffset? CreatedAt);
