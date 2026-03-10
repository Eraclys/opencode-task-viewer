using System.Text.Json;

namespace OpenCode.Client;

public sealed class DelegateOpenCodeService : IOpenCodeService
{
    readonly Func<string, OpenCodeRequest, Task<string?>> _openCodeFetch;

    public DelegateOpenCodeService(Func<string, OpenCodeRequest, Task<string?>> openCodeFetch)
    {
        _openCodeFetch = openCodeFetch;
    }

    public string? BuildSessionUrl(string sessionId, string? directory) => null;

    public async Task<Dictionary<string, SessionRuntimeStatus>> ReadWorkingStatusMapAsync(string directory, CancellationToken cancellationToken = default)
    {
        var data = await _openCodeFetch(
            "/session/status",
            new OpenCodeRequest
            {
                Directory = directory
            });

        return OpenCodeStatusParsers.ParseWorkingStatusMap(data);
    }

    public Task<List<OpenCodeTodo>> ReadTodosAsync(string sessionId, string? directory, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<List<OpenCodeMessage>> ReadMessagesAsync(string sessionId, int? limit = null, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<List<OpenCodeProject>> ReadProjectsAsync(CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<List<OpenCodeSession>> ReadSessionsAsync(string directory, int limit, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<OpenCodeSession?> ReadSessionAsync(string sessionId, string? directory, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<DateTimeOffset?> ArchiveSessionAsync(string sessionId, string? directory, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public async Task<string> CreateSessionAsync(string directory, string title, CancellationToken cancellationToken = default)
    {
        var created = await _openCodeFetch(
            "/session",
            new OpenCodeRequest
            {
                Method = "POST",
                Directory = directory,
                JsonBody = JsonSerializer.Serialize(
                    new
                    {
                        title
                    })
            });

        var sessionId = OpenCodeDispatchParsers.ParseCreatedSessionId(created);

        if (string.IsNullOrWhiteSpace(sessionId))
            throw new InvalidOperationException("OpenCode did not return a session id");

        return sessionId;
    }

    public async Task SendPromptAsync(
        string directory,
        string sessionId,
        string prompt,
        CancellationToken cancellationToken = default)
    {
        await _openCodeFetch(
            $"/session/{Uri.EscapeDataString(sessionId)}/prompt_async",
            new OpenCodeRequest
            {
                Method = "POST",
                Directory = directory,
                JsonBody = JsonSerializer.Serialize(
                    new
                    {
                        parts = new[]
                        {
                            new
                            {
                                type = "text",
                                text = prompt
                            }
                        }
                    })
            });
    }
}
