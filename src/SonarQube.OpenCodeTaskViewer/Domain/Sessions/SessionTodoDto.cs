using System.Text.Json.Serialization;

namespace SonarQube.OpenCodeTaskViewer.Domain.Sessions;

public sealed record SessionTodoDto(string Content, ViewerTaskStatus Status, string? Priority)
{
    [JsonIgnore] public ViewerTaskStatus TaskStatus => Status;

    [JsonPropertyName("status")] public string StatusValue => Status.Value;
}
