using System.Text.Json.Serialization;

namespace SonarQube.OpenCodeTaskViewer.Server.Api;

public sealed class ClearQueuedResponseDto
{
    [JsonPropertyName("cleared")] public required int Cleared { get; init; }
}
