namespace TaskViewer.Server.Application.Orchestration;

public interface IDispatchFailurePolicy
{
    DispatchFailureDecision Decide(int attemptCount, int maxAttempts, DateTimeOffset utcNow);
}

public sealed record DispatchFailureDecision(string State, string? NextAttemptAt);
