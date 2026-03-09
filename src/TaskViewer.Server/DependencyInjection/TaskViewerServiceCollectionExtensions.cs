using TaskViewer.Domain;
using TaskViewer.Domain.Orchestration;
using TaskViewer.Domain.Sessions;
using TaskViewer.Infrastructure.OpenCode;
using TaskViewer.Infrastructure.Orchestration;
using TaskViewer.Infrastructure.ServerSentEvents;
using TaskViewer.OpenCode;
using TaskViewer.Persistence;
using TaskViewer.Server.BackgroundServices;
using TaskViewer.Server.Configuration;
using TaskViewer.Server.Infrastructure.ServerSentEvents;
using TaskViewer.SonarQube;

namespace TaskViewer.Server.DependencyInjection;

internal static class TaskViewerServiceCollectionExtensions
{
    internal static IServiceCollection AddTaskViewerServerInfrastructure(
        this IServiceCollection services)
    {
        services.AddMemoryCache();
        services.AddTaskViewerOpenCode(
            sp =>
            {
                var runtimeSettings = sp.GetRequiredService<AppRuntimeSettings>();

                return new OpenCodeClientOptions(
                    runtimeSettings.OpenCode.Url,
                    runtimeSettings.OpenCode.Username,
                    runtimeSettings.OpenCode.Password);
            });
        services.AddTaskViewerSonarQube(
            sp =>
            {
                var runtimeSettings = sp.GetRequiredService<AppRuntimeSettings>();
                return new SonarQubeClientOptions(runtimeSettings.SonarQube.Url, runtimeSettings.SonarQube.Token);
            });
        services.AddTaskViewerPersistence(
            sp =>
            {
                var runtimeSettings = sp.GetRequiredService<AppRuntimeSettings>();
                return (runtimeSettings.Orchestration.DbPath, () => _ = sp.GetRequiredService<OpenCodeViewerUpdateNotifier>().InvalidateAllAndBroadcastAsync());
            });
        services.AddSingleton<FakeSonarQubeService>();
        services.AddSingleton<ISonarQubeService>(
            sp =>
            {
                var runtimeSettings = sp.GetRequiredService<AppRuntimeSettings>();

                return string.Equals(runtimeSettings.SonarQube.Mode, SonarQubeMode.Fake, StringComparison.Ordinal)
                    ? sp.GetRequiredService<FakeSonarQubeService>()
                    : sp.GetRequiredService<SonarQubeService>();
            });
        services.AddSingleton<SseHub>();
        services.AddSingleton<ISseHub>(sp => sp.GetRequiredService<SseHub>());
        services.AddSingleton<SessionTodoViewService>();
        services.AddSingleton<OpenCodeViewerState>();
        services.AddSingleton<OpenCodeViewerCachePolicy>();
        services.AddSingleton<OpenCodeCacheInvalidationPolicy>();
        services.AddSingleton<OpenCodeSessionSearchService>();
        services.AddSingleton<OpenCodeTasksOverviewService>();
        services.AddSingleton<OpenCodeEventHandler>();
        services.AddSingleton<QueueItemSessionSummaryMapper>();
        services.AddSingleton<OpenCodeViewerUpdateNotifier>();
        services.AddSingleton<ISonarRuleReadService>(sp => new CachedSonarRuleReadService(sp.GetRequiredService<ISonarQubeService>()));
        services.AddSingleton<ISonarRulesReadService>(
            sp => new SonarRulesReadService(
                sp.GetRequiredService<ISonarQubeService>(),
                sp.GetRequiredService<ISonarRuleReadService>()));
        services.AddSingleton<ISonarIssuesReadService>(sp => new SonarIssuesReadService(sp.GetRequiredService<ISonarQubeService>()));
        services.AddSingleton(
            sp =>
            {
                var runtimeSettings = sp.GetRequiredService<AppRuntimeSettings>();

                return new SonarOrchestrator(
                    new SonarOrchestratorOptions
                    {
                        SonarQubeService = sp.GetRequiredService<ISonarQubeService>(),
                        SonarRuleReadService = sp.GetRequiredService<ISonarRuleReadService>(),
                        SonarRulesReadService = sp.GetRequiredService<ISonarRulesReadService>(),
                        SonarIssuesReadService = sp.GetRequiredService<ISonarIssuesReadService>(),
                        OpenCodeApiClient = sp.GetRequiredService<IOpenCodeService>(),
                        SonarUrl = runtimeSettings.SonarQube.Url,
                        SonarToken = runtimeSettings.SonarQube.Token,
                        DbPath = runtimeSettings.Orchestration.DbPath,
                        MaxActive = runtimeSettings.Orchestration.MaxActive,
                        PerProjectMaxActive = runtimeSettings.Orchestration.PerProjectMaxActive,
                        PollMs = runtimeSettings.Orchestration.PollMs,
                        LeaseSeconds = runtimeSettings.Orchestration.LeaseSeconds,
                        MaxAttempts = runtimeSettings.Orchestration.MaxAttempts,
                        MaxWorkingGlobal = runtimeSettings.Orchestration.MaxWorkingGlobal,
                        WorkingResumeBelow = runtimeSettings.Orchestration.WorkingResumeBelow,
                        Persistence = sp.GetRequiredService<IOrchestrationPersistence>(),
                        TaskReadinessGate = new AlwaysReadyGate(),
                        NormalizeDirectory = DirectoryPath.Normalize,
                        BuildOpenCodeSessionUrl = sp.GetRequiredService<OpenCodeSessionSearchService>().BuildOpenCodeSessionUrl,
                        OnChange = () => _ = sp.GetRequiredService<OpenCodeViewerUpdateNotifier>().InvalidateAllAndBroadcastAsync()
                    });
            });
        services.AddHostedService<SonarOrchestratorHostedService>();
        services.AddHostedService<OpenCodeUpstreamSseHostedService>();
        return services;
    }

    internal static IServiceCollection AddTaskViewerServerApplication(
        this IServiceCollection services)
    {
        services.AddSingleton<ISessionsUseCases>(
            sp => new SessionsUseCases(
                sp.GetRequiredService<OpenCodeSessionSearchService>(),
                sp.GetRequiredService<SonarOrchestrator>(),
                sp.GetRequiredService<QueueItemSessionSummaryMapper>(),
                sp.GetRequiredService<SessionTodoViewService>(),
                sp.GetRequiredService<OpenCodeViewerUpdateNotifier>()));
        services.AddSingleton<IOrchestrationUseCases>(sp => new OrchestrationUseCases(sp.GetRequiredService<SonarOrchestrator>()));

        return services;
    }
}
