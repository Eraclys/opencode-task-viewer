namespace TaskViewer.Server.Application.Orchestration;

public interface IWorkingSessionsReadService
{
    Task<WorkingSessionsSample> GetWorkingSessionsCountAsync(bool forceRefresh, int pollMs);
}
