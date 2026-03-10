namespace OpenCode.Client;

public sealed class OpenCodeRequest
{
    public string Method { get; init; } = "GET";
    public Dictionary<string, string?> Query { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public string? Directory { get; init; }
    public string? JsonBody { get; init; }
}
