using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TaskViewer.Server.Configuration;

namespace TaskViewer.Server;

sealed class SonarOrchestratorHostedService : BackgroundService
{
    readonly ILogger<SonarOrchestratorHostedService> _logger;
    readonly SonarOrchestrator _orchestrator;
    readonly OrchestrationRuntimeSettings _settings;

    public SonarOrchestratorHostedService(
        SonarOrchestrator orchestrator,
        AppRuntimeSettings runtimeSettings,
        ILogger<SonarOrchestratorHostedService> logger)
    {
        _orchestrator = orchestrator;
        _settings = runtimeSettings.Orchestration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await ExecuteTickSafelyAsync(stoppingToken);

        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(_settings.PollMs));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken))
                    break;

                await ExecuteTickSafelyAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    async Task ExecuteTickSafelyAsync(CancellationToken stoppingToken)
    {
        if (stoppingToken.IsCancellationRequested)
            return;

        try
        {
            await _orchestrator.Tick();
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Sonar orchestrator background tick failed.");
        }
    }
}
