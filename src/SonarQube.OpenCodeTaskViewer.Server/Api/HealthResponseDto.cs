using System.Text.Json.Serialization;

namespace SonarQube.OpenCodeTaskViewer.Server.Api;

public sealed class HealthResponseDto
{
    [JsonPropertyName("ok")] public required bool Ok { get; init; }
}
