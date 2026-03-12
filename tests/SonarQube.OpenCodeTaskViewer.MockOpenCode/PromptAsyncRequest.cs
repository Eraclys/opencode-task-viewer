namespace SonarQube.OpenCodeTaskViewer.MockOpenCode;

sealed class PromptAsyncRequest
{
    public List<PromptPartRequest>? Parts { get; init; }
}