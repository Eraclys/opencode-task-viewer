using Microsoft.Extensions.DependencyInjection;

namespace SonarQube.Client;

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

        services.AddTransient(sp => new SonarQubeHttpClient(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(ApiHttpClientName),
            optionsFactory(sp)));

        services.AddSingleton(sp => new SonarQubeService(() => sp.GetRequiredService<SonarQubeHttpClient>()));
        services.AddSingleton<ISonarQubeService>(sp => sp.GetRequiredService<SonarQubeService>());

        return services;
    }
}
