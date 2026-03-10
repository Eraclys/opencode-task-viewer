namespace SonarQube.OpenCodeTaskViewer.Server.Configuration;

public sealed record AppRuntimeSettings(
    ViewerRuntimeSettings Viewer,
    OpenCodeRuntimeSettings OpenCode,
    SonarQubeRuntimeSettings SonarQube,
    OrchestrationRuntimeSettings Orchestration);
