using TaskViewer.Domain.Sessions;

namespace TaskViewer.OpenCode;

public interface IOpenCodeService
{
    string? BuildSessionUrl(string sessionId, string? directory);
    Task<Dictionary<string, SessionRuntimeStatus>> ReadWorkingStatusMapAsync(string directory, CancellationToken cancellationToken = default);
    Task<List<OpenCodeTodo>> ReadTodosAsync(string sessionId, string? directory, CancellationToken cancellationToken = default);
    Task<List<OpenCodeMessage>> ReadMessagesAsync(string sessionId, int? limit = null, CancellationToken cancellationToken = default);
    Task<List<OpenCodeProject>> ReadProjectsAsync(CancellationToken cancellationToken = default);
    Task<List<OpenCodeSession>> ReadSessionsAsync(string directory, int limit, CancellationToken cancellationToken = default);
    Task<OpenCodeSession?> ReadSessionAsync(string sessionId, string? directory, CancellationToken cancellationToken = default);
    Task<DateTimeOffset?> ArchiveSessionAsync(string sessionId, string? directory, CancellationToken cancellationToken = default);
    Task<string> CreateSessionAsync(string directory, string title, CancellationToken cancellationToken = default);

    Task SendPromptAsync(
        string directory,
        string sessionId,
        string prompt,
        CancellationToken cancellationToken = default);
}
