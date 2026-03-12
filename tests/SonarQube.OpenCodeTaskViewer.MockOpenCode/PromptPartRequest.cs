namespace SonarQube.OpenCodeTaskViewer.MockOpenCode;

sealed class PromptPartRequest
{
    public string? Type { get; init; }
    public string? Text { get; init; }
}