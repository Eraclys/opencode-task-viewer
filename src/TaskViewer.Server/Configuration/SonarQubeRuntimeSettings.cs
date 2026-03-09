namespace TaskViewer.Server.Configuration;

public sealed record SonarQubeRuntimeSettings(string Url, string Token, string Mode);
