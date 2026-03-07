namespace TaskViewer.Server.Application.Orchestration;

public sealed class OrchestratorRuntime : IOrchestratorRuntime
{
    volatile bool _disposed;
    Task? _loopTask;
    volatile bool _tickRunning;
    PeriodicTimer? _timer;

    public async Task RunOnceAsync(Func<Task> tickBody)
    {
        if (_tickRunning || _disposed)
            return;

        _tickRunning = true;

        try
        {
            if (_disposed)
                return;

            await tickBody();
        }
        finally
        {
            _tickRunning = false;
        }
    }

    public void Start(int pollMs, CancellationToken stoppingToken, Func<Task> tick)
    {
        if (_timer is not null || _disposed)
            return;

        _timer = new PeriodicTimer(TimeSpan.FromMilliseconds(pollMs));

        _loopTask = Task.Run(
            async () =>
            {
                await tick();

                while (!stoppingToken.IsCancellationRequested &&
                       _timer is not null)
                {
                    try
                    {
                        if (!await _timer.WaitForNextTickAsync(stoppingToken))
                            break;

                        await tick();
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch
                    {
                    }
                }
            },
            stoppingToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;

        if (_timer is not null)
        {
            _timer.Dispose();
            _timer = null;
        }

        if (_loopTask is null)
            return;

        try
        {
            await _loopTask;
        }
        catch
        {
        }
    }
}
