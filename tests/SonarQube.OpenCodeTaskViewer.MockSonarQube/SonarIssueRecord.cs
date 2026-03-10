namespace SonarQube.OpenCodeTaskViewer.MockSonarQube;

sealed class SonarIssueRecord
{
    public string Key { get; set; } = string.Empty;
    public string Component { get; set; } = string.Empty;
    public int Line { get; set; }
    public string Rule { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}
