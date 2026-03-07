using System.Collections.Concurrent;
using System.Text.Json;

sealed class SseHub
{
    readonly ConcurrentDictionary<Guid, SseClient> _clients = new();

    public SseClient AddClient(HttpResponse response, CancellationToken cancellationToken)
    {
        var client = new SseClient(response, cancellationToken, Remove);
        _clients[client.Id] = client;
        client.Start();

        return client;
    }

    void Remove(Guid id) => _clients.TryRemove(id, out _);

    public async Task Broadcast(object data)
    {
        var payload = JsonSerializer.Serialize(data);
        var tasks = _clients.Values.Select(c => c.SendRaw($"data: {payload}\n\n"));
        await Task.WhenAll(tasks);
    }
}