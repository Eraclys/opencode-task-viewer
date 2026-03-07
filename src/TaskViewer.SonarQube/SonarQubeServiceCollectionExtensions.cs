using Microsoft.Extensions.DependencyInjection;

namespace TaskViewer.SonarQube;

public static class SonarQubeServiceCollectionExtensions
{
    const string ApiHttpClientName = "SonarQubeApi";

    public static IServiceCollection AddTaskViewerSonarQube(
        this IServiceCollection services,
        Func<IServiceProvider, SonarQubeClientOptions> optionsFactory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(optionsFactory);

        services.AddHttpClient(ApiHttpClientName, client => client.Timeout = TimeSpan.FromSeconds(60));
        services.AddTransient(
            sp => new SonarQubeTypedHttpClient(
                sp.GetRequiredService<IHttpClientFactory>().CreateClient(ApiHttpClientName),
                optionsFactory(sp)));
        services.AddSingleton(
            sp => new SonarQubeApiClient(
                () => sp.GetRequiredService<SonarQubeTypedHttpClient>()));
        services.AddSingleton<ISonarQubeService>(sp => sp.GetRequiredService<SonarQubeApiClient>());

        return services;
    }
}
