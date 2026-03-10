using Microsoft.Extensions.Options;
using SonarQube.OpenCodeTaskViewer.Server.Configuration;

namespace SonarQube.OpenCodeTaskViewer.Server.DependencyInjection;

static class TaskViewerConfigurationExtensions
{
    internal static AppRuntimeSettings AddTaskViewerRuntimeSettings(this WebApplicationBuilder builder)
    {
        builder.Services.AddSingleton<IValidateOptions<AppRuntimeSettingsOptions>, AppRuntimeSettingsOptionsValidator>();

        builder
            .Services
            .AddOptions<AppRuntimeSettingsOptions>()
            .Configure<IConfiguration>((options, configuration) => AppRuntimeSettingsFactory.BindInto(options, configuration))
            .ValidateOnStart();

        var settings = AppRuntimeSettingsLoader.Load(builder.Configuration, builder.Environment);
        builder.Services.AddSingleton(settings);

        return settings;
    }
}
