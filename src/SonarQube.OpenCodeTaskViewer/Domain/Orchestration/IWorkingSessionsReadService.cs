namespace SonarQube.OpenCodeTaskViewer.Domain.Orchestration;

public interface IWorkingSessionsReadService
{
    Task<WorkingSessionsSample> GetWorkingSessionsCountAsync(bool forceRefresh, int pollMs);
}
