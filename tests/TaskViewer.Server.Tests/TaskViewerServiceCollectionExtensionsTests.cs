using Microsoft.Extensions.DependencyInjection;
using TaskViewer.OpenCode;
using TaskViewer.Server.Application.Orchestration;
using TaskViewer.Server.Configuration;
using TaskViewer.Server.DependencyInjection;
using TaskViewer.SonarQube;

namespace TaskViewer.Server.Tests;

public sealed class TaskViewerServiceCollectionExtensionsTests
{
    [Fact]
    public async Task AddTaskViewerServerInfrastructure_RegistersDirectAdr0006Boundaries()
    {
        var services = new ServiceCollection();
        services.AddSingleton(
            new AppRuntimeSettings(
                new ViewerRuntimeSettings("127.0.0.1", 8080),
                new OpenCodeRuntimeSettings("http://localhost:4096", "opencode", "secret"),
                new SonarQubeRuntimeSettings("http://sonar.local", "token"),
                new OrchestrationRuntimeSettings(
                    Path.Combine(Path.GetTempPath(), $"taskviewer-di-{Guid.NewGuid():N}.sqlite"),
                    MaxActive: 1,
                    PollMs: 1000,
                    MaxAttempts: 3,
                    MaxWorkingGlobal: 5,
                    WorkingResumeBelow: 3)));

        services
            .AddTaskViewerServerInfrastructure()
            .AddTaskViewerServerApplication();

        await using var provider = services.BuildServiceProvider();

        var openCodeApiClient = provider.GetRequiredService<OpenCodeApiClient>();
        var openCodeStatusReader = provider.GetRequiredService<IOpenCodeStatusReader>();
        var openCodeDispatchClient = provider.GetRequiredService<IOpenCodeDispatchClient>();
        var sonarQubeApiClient = provider.GetRequiredService<SonarQubeApiClient>();
        var sonarQubeService = provider.GetRequiredService<ISonarQubeService>();
        var orchestrator = provider.GetRequiredService<SonarOrchestrator>();
        var orchestrationUseCases = provider.GetRequiredService<IOrchestrationUseCases>();
        var orchestrationGateway = provider.GetService<IOrchestrationGateway>();

        Assert.Same(openCodeApiClient, openCodeStatusReader);
        Assert.Same(openCodeApiClient, openCodeDispatchClient);
        Assert.Same(sonarQubeApiClient, sonarQubeService);
        Assert.IsType<OrchestrationUseCases>(orchestrationUseCases);
        Assert.Null(orchestrationGateway);
        Assert.NotNull(orchestrator);
    }
}
