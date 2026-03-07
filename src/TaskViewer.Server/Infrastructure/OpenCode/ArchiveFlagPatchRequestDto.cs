using System.Text.Json.Serialization;

namespace TaskViewer.Server.Infrastructure.OpenCode;

sealed class ArchiveFlagPatchRequestDto
{
    [JsonPropertyName("archived")]
    public required bool Archived { get; init; }
}
