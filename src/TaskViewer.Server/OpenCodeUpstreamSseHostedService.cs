using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TaskViewer.Infrastructure.OpenCode;
using TaskViewer.OpenCode;

namespace TaskViewer.Server;

sealed class OpenCodeUpstreamSseHostedService : BackgroundService
{
    readonly OpenCodeEventHandler _eventHandler;
    readonly ILogger<OpenCodeUpstreamSseHostedService> _logger;
    readonly OpenCodeUpstreamSseService _upstreamSseService;

    public OpenCodeUpstreamSseHostedService(
        OpenCodeUpstreamSseService upstreamSseService,
        OpenCodeEventHandler eventHandler,
        ILogger<OpenCodeUpstreamSseHostedService> logger)
    {
        _upstreamSseService = upstreamSseService;
        _eventHandler = eventHandler;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await _upstreamSseService.RunAsync(_eventHandler.HandleAsync, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OpenCode upstream SSE background loop stopped unexpectedly.");
            throw;
        }
    }
}
