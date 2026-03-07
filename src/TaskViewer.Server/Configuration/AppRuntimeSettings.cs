namespace TaskViewer.Server.Configuration;

public sealed record AppRuntimeSettings(
    ViewerRuntimeSettings Viewer,
    OpenCodeRuntimeSettings OpenCode,
    SonarQubeRuntimeSettings SonarQube,
    OrchestrationRuntimeSettings Orchestration);
