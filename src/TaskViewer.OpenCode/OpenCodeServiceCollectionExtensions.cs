using Microsoft.Extensions.DependencyInjection;

namespace TaskViewer.OpenCode;

public static class OpenCodeServiceCollectionExtensions
{
    const string ApiHttpClientName = "OpenCodeApi";
    const string SseHttpClientName = "OpenCodeSse";

    public static IServiceCollection AddTaskViewerOpenCode(
        this IServiceCollection services,
        Func<IServiceProvider, OpenCodeClientOptions> optionsFactory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(optionsFactory);

        services.AddHttpClient(ApiHttpClientName, client => client.Timeout = TimeSpan.FromSeconds(60));
        services.AddHttpClient(SseHttpClientName, client => client.Timeout = Timeout.InfiniteTimeSpan);
        services.AddTransient(
            sp => new OpenCodeApiHttpClient(
                sp.GetRequiredService<IHttpClientFactory>().CreateClient(ApiHttpClientName),
                optionsFactory(sp)));
        services.AddTransient(
            sp => new OpenCodeSseHttpClient(
                sp.GetRequiredService<IHttpClientFactory>().CreateClient(SseHttpClientName),
                optionsFactory(sp)));
        services.AddSingleton(
            sp => new OpenCodeApiClient(
                () => sp.GetRequiredService<OpenCodeApiHttpClient>(),
                optionsFactory(sp)));
        services.AddSingleton<IOpenCodeStatusReader>(sp => sp.GetRequiredService<OpenCodeApiClient>());
        services.AddSingleton<IOpenCodeDispatchClient>(sp => sp.GetRequiredService<OpenCodeApiClient>());
        services.AddSingleton(sp => new OpenCodeUpstreamSseService(sp.GetRequiredService<OpenCodeSseHttpClient>));

        return services;
    }
}
