using TaskViewer.Server.Configuration;
using Microsoft.Extensions.Options;

namespace TaskViewer.Server.DependencyInjection;

internal static class TaskViewerConfigurationExtensions
{
    internal static AppRuntimeSettings AddTaskViewerRuntimeSettings(this WebApplicationBuilder builder)
    {
        builder.Services.AddSingleton<IValidateOptions<AppRuntimeSettingsOptions>, AppRuntimeSettingsOptionsValidator>();
        builder.Services
            .AddOptions<AppRuntimeSettingsOptions>()
            .Configure<IConfiguration>((options, configuration) => AppRuntimeSettingsFactory.BindInto(options, configuration))
            .ValidateOnStart();

        var settings = AppRuntimeSettingsLoader.Load(builder.Configuration, builder.Environment);
        builder.Services.AddSingleton(settings);
        return settings;
    }
}
