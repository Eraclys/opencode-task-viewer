namespace SonarQube.OpenCodeTaskViewer.Server.Configuration;

public static class AppRuntimeSettingsLoader
{
    public static AppRuntimeSettings Load(
        IConfiguration configuration,
        IHostEnvironment environment)
        => AppRuntimeSettingsFactory.Create(AppRuntimeSettingsFactory.Bind(configuration), environment);
}
