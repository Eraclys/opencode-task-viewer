namespace TaskViewer.Server.Application.Sessions;

public interface ISessionsUseCases
{
    Task<IReadOnlyList<SessionSummaryDto>> ListSessionsAsync(string? limitParam);
    Task<SessionTasksResult> GetSessionTasksAsync(string sessionId);
    Task<LastAssistantMessageResult> GetLastAssistantMessageAsync(string sessionId);
    Task<ArchiveSessionResult> ArchiveSessionAsync(string sessionId);
}

public sealed record SessionTasksResult(bool Found, IReadOnlyList<ViewerTaskDto> Tasks);

public sealed record LastAssistantMessageResult(
    bool Found,
    string SessionId,
    string? Message,
    string? CreatedAt);

public sealed record ArchiveSessionResult(bool Found, string? ArchivedAt);
