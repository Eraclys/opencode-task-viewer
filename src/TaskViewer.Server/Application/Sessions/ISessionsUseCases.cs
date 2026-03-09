using System.Collections.Generic;
using System.Threading.Tasks;

namespace TaskViewer.Server.Application.Sessions;

public interface ISessionsUseCases
{
    Task<IReadOnlyList<SessionSummaryDto>> ListSessionsAsync(string? limitParam);
    Task<SessionTasksResult> GetSessionTasksAsync(string sessionId);
    Task<LastAssistantMessageResult> GetTaskLastAssistantMessageAsync(string taskId);
    Task<LastAssistantMessageResult> GetLastAssistantMessageAsync(string sessionId);
    Task<ArchiveSessionResult> ArchiveSessionAsync(string sessionId);
}
