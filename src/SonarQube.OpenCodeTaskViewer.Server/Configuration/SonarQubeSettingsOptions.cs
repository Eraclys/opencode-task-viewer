namespace SonarQube.OpenCodeTaskViewer.Server.Configuration;

public sealed class SonarQubeSettingsOptions
{
    public string? Url { get; set; }
    public string? Token { get; set; }
    public string? Mode { get; set; }
}