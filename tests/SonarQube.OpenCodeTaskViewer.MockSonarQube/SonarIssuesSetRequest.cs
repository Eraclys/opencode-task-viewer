namespace SonarQube.OpenCodeTaskViewer.MockSonarQube;

sealed class SonarIssuesSetRequest
{
    public List<SonarIssueSetPayload>? Issues { get; init; }
}