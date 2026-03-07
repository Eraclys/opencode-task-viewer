using System.Text.Json.Serialization;

namespace TaskViewer.Server.Infrastructure.OpenCode;

sealed class ArchiveTimeDto
{
    [JsonPropertyName("archived")]
    public required long Archived { get; init; }
}
