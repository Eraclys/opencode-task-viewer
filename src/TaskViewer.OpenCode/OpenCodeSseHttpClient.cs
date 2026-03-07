using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

namespace TaskViewer.OpenCode;

public sealed class OpenCodeSseHttpClient
{
    readonly HttpClient _httpClient;
    readonly OpenCodeClientOptions _options;

    public OpenCodeSseHttpClient(HttpClient httpClient, OpenCodeClientOptions options)
    {
        _httpClient = httpClient;
        _options = options;
    }

    public async Task ReadEventStreamAsync(Func<JsonNode, Task> handleEventAsync, CancellationToken cancellationToken)
    {
        var url = new Uri(new Uri(_options.Url), "/global/event");
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("Accept", "text/event-stream");
        ApplyAuthentication(request);

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        if (!response.IsSuccessStatusCode ||
            response.Content is null)
            throw new InvalidOperationException($"Upstream SSE failed: {(int)response.StatusCode} {response.ReasonPhrase}");

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var buffer = new StringBuilder();

        while (!reader.EndOfStream &&
               !cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken) ?? string.Empty;

            if (line.Length == 0)
            {
                var payload = TryParsePayload(buffer.ToString());
                buffer.Clear();

                if (payload is not null)
                    await handleEventAsync(payload);

                continue;
            }

            buffer.Append(line.Replace("\r", string.Empty)).Append('\n');
        }
    }

    void ApplyAuthentication(HttpRequestMessage request)
    {
        if (string.IsNullOrWhiteSpace(_options.Password))
            return;

        var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.Username}:{_options.Password}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);
    }

    static JsonNode? TryParsePayload(string rawEvent)
    {
        if (string.IsNullOrWhiteSpace(rawEvent))
            return null;

        var dataLines = rawEvent
            .Split('\n')
            .Where(line => line.StartsWith("data:", StringComparison.Ordinal))
            .ToList();

        if (dataLines.Count == 0)
            return null;

        var data = string.Join("\n", dataLines.Select(line => line[5..].Trim()));

        if (string.IsNullOrWhiteSpace(data))
            return null;

        try
        {
            return JsonNode.Parse(data);
        }
        catch
        {
            return null;
        }
    }
}
