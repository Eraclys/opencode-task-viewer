using TaskViewer.Server.Configuration;

namespace TaskViewer.Server.DependencyInjection;

internal static class TaskViewerConfigurationExtensions
{
    internal static AppRuntimeSettings AddTaskViewerRuntimeSettings(this WebApplicationBuilder builder)
    {
        var settings = AppRuntimeSettingsLoader.Load(builder.Configuration, builder.Environment);
        builder.Services.AddSingleton(settings);
        return settings;
    }
}
