using System.Text.Json.Serialization;

namespace SonarQube.OpenCodeTaskViewer.Server.Api;

public sealed class ArchiveSessionResponseDto
{
    [JsonPropertyName("ok")] public required bool Ok { get; init; }

    [JsonPropertyName("archivedAt")] public DateTimeOffset? ArchivedAt { get; init; }
}
