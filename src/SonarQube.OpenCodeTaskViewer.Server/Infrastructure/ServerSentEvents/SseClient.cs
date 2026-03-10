using System.Text.Json;
using System.Threading.Channels;

namespace SonarQube.OpenCodeTaskViewer.Server.Infrastructure.ServerSentEvents;

sealed class SseClient
{
    readonly Action<Guid> _onDone;
    readonly Channel<string> _queue = Channel.CreateUnbounded<string>();
    readonly HttpResponse _response;
    readonly CancellationToken _token;

    public SseClient(HttpResponse response, CancellationToken token, Action<Guid> onDone)
    {
        _response = response;
        _token = token;
        _onDone = onDone;
    }

    public Guid Id { get; } = Guid.NewGuid();
    public Task Completion { get; private set; } = Task.CompletedTask;

    public void Start()
    {
        Completion = Task.Run(
            async () =>
            {
                try
                {
                    while (!_token.IsCancellationRequested &&
                           await _queue.Reader.WaitToReadAsync(_token))
                    {
                        while (_queue.Reader.TryRead(out var message))
                        {
                            await _response.WriteAsync(message, _token);
                            await _response.Body.FlushAsync(_token);
                        }
                    }
                }
                catch
                {
                }
                finally
                {
                    _onDone(Id);
                }
            },
            _token);
    }

    public Task Send<T>(T data)
    {
        var payload = JsonSerializer.Serialize(data);

        return SendRaw($"data: {payload}\n\n");
    }

    public Task SendRaw(string message)
    {
        _queue.Writer.TryWrite(message);

        return Task.CompletedTask;
    }
}
