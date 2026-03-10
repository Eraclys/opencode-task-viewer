using TaskViewer.Domain.Sessions;

namespace TaskViewer.OpenCode;

public class DisabledOpenCodeService : IOpenCodeService
{
    public virtual string? BuildSessionUrl(string sessionId, string? directory) => null;

    public virtual async Task<Dictionary<string, SessionRuntimeStatus>> ReadWorkingStatusMapAsync(string directory, CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        return new Dictionary<string, SessionRuntimeStatus>(StringComparer.Ordinal);
    }

    public virtual Task<List<OpenCodeTodo>> ReadTodosAsync(string sessionId, string? directory, CancellationToken cancellationToken = default)
        => Task.FromResult(new List<OpenCodeTodo>());

    public virtual Task<List<OpenCodeMessage>> ReadMessagesAsync(string sessionId, int? limit = null, CancellationToken cancellationToken = default)
        => Task.FromResult(new List<OpenCodeMessage>());

    public virtual Task<List<OpenCodeProject>> ReadProjectsAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(new List<OpenCodeProject>());

    public virtual Task<List<OpenCodeSession>> ReadSessionsAsync(string directory, int limit, CancellationToken cancellationToken = default)
        => Task.FromResult(new List<OpenCodeSession>());

    public virtual Task<OpenCodeSession?> ReadSessionAsync(string sessionId, string? directory, CancellationToken cancellationToken = default)
        => Task.FromResult<OpenCodeSession?>(null);

    public virtual Task<DateTimeOffset?> ArchiveSessionAsync(string sessionId, string? directory, CancellationToken cancellationToken = default)
        => Task.FromResult<DateTimeOffset?>(null);

    public virtual async Task<string> CreateSessionAsync(string directory, string title, CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        throw new InvalidOperationException("OpenCode dispatch is not configured");
    }

    public virtual async Task SendPromptAsync(string directory, string sessionId, string prompt, CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        throw new InvalidOperationException("OpenCode dispatch is not configured");
    }
}
