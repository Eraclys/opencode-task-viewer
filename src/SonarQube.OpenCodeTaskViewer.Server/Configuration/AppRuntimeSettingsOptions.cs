namespace SonarQube.OpenCodeTaskViewer.Server.Configuration;

public sealed class AppRuntimeSettingsOptions
{
    public ViewerSettingsOptions TaskViewer { get; set; } = new();
    public OpenCodeSettingsOptions OpenCode { get; set; } = new();
    public SonarQubeSettingsOptions SonarQube { get; set; } = new();
    public OrchestrationSettingsOptions Orchestration { get; set; } = new();
}

public sealed class ViewerSettingsOptions
{
    public string? Host { get; set; }
    public string? Port { get; set; }
}

public sealed class OpenCodeSettingsOptions
{
    public string? Url { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }
}

public sealed class SonarQubeSettingsOptions
{
    public string? Url { get; set; }
    public string? Token { get; set; }
    public string? Mode { get; set; }
}

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
