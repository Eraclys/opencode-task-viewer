using Microsoft.Extensions.Options;

namespace TaskViewer.Server.Configuration;

sealed class AppRuntimeSettingsOptionsValidator(IHostEnvironment environment) : IValidateOptions<AppRuntimeSettingsOptions>
{
    public ValidateOptionsResult Validate(string? name, AppRuntimeSettingsOptions options)
    {
        try
        {
            _ = AppRuntimeSettingsFactory.Create(options, environment);
            return ValidateOptionsResult.Success;
        }
        catch (InvalidOperationException error)
        {
            return ValidateOptionsResult.Fail(error.Message);
        }
    }
}
