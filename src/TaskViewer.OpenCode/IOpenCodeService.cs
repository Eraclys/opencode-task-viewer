namespace TaskViewer.OpenCode;

public interface IOpenCodeService : IOpenCodeStatusReader, IOpenCodeDispatchClient
{
    string? BuildSessionUrl(string sessionId, string? directory);
    Task<Dictionary<string, string>> ReadWorkingStatusMapAsync(string directory, CancellationToken cancellationToken = default);
    Task<List<OpenCodeTodoTransport>> ReadTodosAsync(string sessionId, string? directory, CancellationToken cancellationToken = default);
    Task<List<OpenCodeMessageTransport>> ReadMessagesAsync(string sessionId, int? limit = null, CancellationToken cancellationToken = default);
    Task<List<OpenCodeProjectTransport>> ReadProjectsAsync(CancellationToken cancellationToken = default);
    Task<List<OpenCodeSessionTransport>> ReadSessionsAsync(string directory, int limit, CancellationToken cancellationToken = default);
    Task<OpenCodeSessionTransport?> ReadSessionAsync(string sessionId, string? directory, CancellationToken cancellationToken = default);
    Task<DateTimeOffset?> ArchiveSessionAsync(string sessionId, string? directory, CancellationToken cancellationToken = default);
    Task<string> CreateSessionAsync(string directory, string title, CancellationToken cancellationToken = default);

    Task SendPromptAsync(
        string directory,
        string sessionId,
        string prompt,
        CancellationToken cancellationToken = default);
}
