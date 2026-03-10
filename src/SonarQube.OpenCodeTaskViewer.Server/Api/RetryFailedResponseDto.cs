using System.Text.Json.Serialization;

namespace SonarQube.OpenCodeTaskViewer.Server.Api;

public sealed class RetryFailedResponseDto
{
    [JsonPropertyName("retried")] public required int Retried { get; init; }
}
