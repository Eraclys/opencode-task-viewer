namespace TaskViewer.Server.Application.Orchestration;

public sealed class DispatchFailurePolicy : IDispatchFailurePolicy
{
    public DispatchFailureDecision Decide(int attemptCount, int maxAttempts, DateTimeOffset utcNow)
    {
        var exhausted = attemptCount >= maxAttempts;

        if (exhausted)
            return new DispatchFailureDecision("failed", null);

        var nextAttemptAt = utcNow.AddMilliseconds(MakeBackoffMs(attemptCount));

        return new DispatchFailureDecision("queued", nextAttemptAt);
    }

    static int MakeBackoffMs(int attempt)
    {
        var n = Math.Max(1, attempt);
        var backoff = 2500 * Math.Pow(2, n - 1);

        return (int)Math.Min(60_000, backoff);
    }
}
