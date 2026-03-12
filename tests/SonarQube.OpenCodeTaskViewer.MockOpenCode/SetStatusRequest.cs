namespace SonarQube.OpenCodeTaskViewer.MockOpenCode;

sealed class SetStatusRequest
{
    public string? Directory { get; init; }
    public string? SessionId { get; init; }
    public string? Type { get; init; }
}