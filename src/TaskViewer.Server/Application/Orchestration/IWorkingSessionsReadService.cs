namespace TaskViewer.Server.Application.Orchestration;

public interface IWorkingSessionsReadService
{
    Task<WorkingSessionsSample> GetWorkingSessionsCountAsync(bool forceRefresh, int pollMs);
}

public sealed record WorkingSessionsSample(DateTimeOffset SampledAt, int Count);
