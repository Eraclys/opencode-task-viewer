namespace SonarQube.OpenCodeTaskViewer.MockOpenCode;

sealed class SessionRecord
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Directory { get; set; } = string.Empty;
    public SessionProjectRecord Project { get; set; } = new();
    public TimeRecord Time { get; set; } = new();
}
