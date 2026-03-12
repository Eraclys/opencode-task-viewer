namespace SonarQube.OpenCodeTaskViewer.Server.Configuration;

public sealed class OrchestrationSettingsOptions
{
    public string? DbPath { get; set; }
    public string? MaxActive { get; set; }
    public string? PerProjectMaxActive { get; set; }
    public string? PollMs { get; set; }
    public string? LeaseSeconds { get; set; }
    public string? MaxAttempts { get; set; }
    public string? MaxWorkingGlobal { get; set; }
    public string? WorkingResumeBelow { get; set; }
}