namespace TaskViewer.Application.Orchestration;

public interface IDispatchFailurePolicy
{
    DispatchFailureDecision Decide(int attemptCount, int maxAttempts, DateTimeOffset utcNow);
}
