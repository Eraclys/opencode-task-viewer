namespace SonarQube.OpenCodeTaskViewer.MockOpenCode;

sealed class SetFailuresRequest
{
    public int? SessionCreateCount { get; init; }
    public int? PromptAsyncCount { get; init; }
    public int? PromptDelayMs { get; init; }
}