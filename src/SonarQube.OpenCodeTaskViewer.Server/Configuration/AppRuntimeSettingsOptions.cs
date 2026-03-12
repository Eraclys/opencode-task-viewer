namespace SonarQube.OpenCodeTaskViewer.Server.Configuration;

public sealed class AppRuntimeSettingsOptions
{
    public ViewerSettingsOptions TaskViewer { get; set; } = new();
    public OpenCodeSettingsOptions OpenCode { get; set; } = new();
    public SonarQubeSettingsOptions SonarQube { get; set; } = new();
    public OrchestrationSettingsOptions Orchestration { get; set; } = new();
}