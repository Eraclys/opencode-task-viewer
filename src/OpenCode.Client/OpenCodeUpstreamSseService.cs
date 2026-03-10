namespace OpenCode.Client;

public sealed class OpenCodeUpstreamSseService
{
    readonly Func<OpenCodeSseHttpClient> _createClient;

    public OpenCodeUpstreamSseService(Func<OpenCodeSseHttpClient> createClient)
    {
        _createClient = createClient;
    }

    public async Task RunAsync(Func<OpenCodeSseEvent, Task> handleEventAsync, CancellationToken cancellationToken)
    {
        var retryDelayMs = 1000;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await _createClient().ReadEventStreamAsync(handleEventAsync, cancellationToken);
                retryDelayMs = 1000;
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                try
                {
                    await Task.Delay(retryDelayMs, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                retryDelayMs = Math.Min(retryDelayMs * 2, 30000);
            }
        }
    }
}
