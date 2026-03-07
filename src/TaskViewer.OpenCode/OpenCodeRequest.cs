using System.Text.Json.Nodes;

namespace TaskViewer.OpenCode;

public sealed class OpenCodeRequest
{
    public string Method { get; init; } = "GET";
    public Dictionary<string, string?> Query { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public string? Directory { get; init; }
    public JsonNode? JsonBody { get; init; }
}
