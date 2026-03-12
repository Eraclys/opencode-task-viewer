namespace SonarQube.OpenCodeTaskViewer.MockOpenCode;

sealed class SetTodosRequest
{
    public string? SessionId { get; init; }
    public List<TodoRecord>? Todos { get; init; }
}