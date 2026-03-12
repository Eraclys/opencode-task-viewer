using System.Text.Json;

namespace SonarQube.OpenCodeTaskViewer.MockOpenCode;

sealed class EmitRequest
{
    public string? Directory { get; init; }
    public string? Type { get; init; }
    public JsonElement? Properties { get; init; }
}