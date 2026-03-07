using System.Text.Json.Serialization;

namespace TaskViewer.Server.Infrastructure.OpenCode;

sealed class ArchiveTimePatchRequestDto
{
    [JsonPropertyName("time")]
    public required ArchiveTimeDto Time { get; init; }
}
