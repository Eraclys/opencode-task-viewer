namespace SonarQube.OpenCodeTaskViewer.MockOpenCode;

sealed class ArchiveSessionRequest
{
    public ArchiveTimeRequest? Time { get; init; }
    public bool? Archived { get; init; }
}