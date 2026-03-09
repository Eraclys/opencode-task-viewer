namespace TaskViewer.Domain.Sessions;

public interface ISessionsUseCases
{
    Task<IReadOnlyList<SessionSummaryDto>> ListSessionsAsync(string? limitParam, CancellationToken cancellationToken = default);
    Task<SessionTasksResult> GetSessionTasksAsync(string sessionId, CancellationToken cancellationToken = default);
    Task<LastAssistantMessageResult> GetTaskLastAssistantMessageAsync(string taskId, CancellationToken cancellationToken = default);
    Task<LastAssistantMessageResult> GetLastAssistantMessageAsync(string sessionId, CancellationToken cancellationToken = default);
    Task<ArchiveSessionResult> ArchiveSessionAsync(string sessionId, CancellationToken cancellationToken = default);
}
