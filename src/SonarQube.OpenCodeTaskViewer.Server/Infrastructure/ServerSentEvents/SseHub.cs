using System.Collections.Concurrent;
using System.Text.Json;
using SonarQube.OpenCodeTaskViewer.Infrastructure.ServerSentEvents;

namespace SonarQube.OpenCodeTaskViewer.Server.Infrastructure.ServerSentEvents;

sealed class SseHub : ISseHub
{
    readonly ConcurrentDictionary<Guid, SseClient> _clients = new();

    public async Task Broadcast<T>(T data)
    {
        var payload = JsonSerializer.Serialize(data);
        var tasks = _clients.Values.Select(c => c.SendRaw($"data: {payload}\n\n"));
        await Task.WhenAll(tasks);
    }

    public SseClient AddClient(HttpResponse response, CancellationToken cancellationToken)
    {
        var client = new SseClient(response, cancellationToken, Remove);
        _clients[client.Id] = client;
        client.Start();

        return client;
    }

    void Remove(Guid id) => _clients.TryRemove(id, out _);
}
