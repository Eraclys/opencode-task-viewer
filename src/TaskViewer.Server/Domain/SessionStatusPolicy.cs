namespace TaskViewer.Server.Domain;

public static class SessionStatusPolicy
{
    public static bool IsRuntimeRunning(string? type)
    {
        var normalized = (type ?? string.Empty).Trim().ToLowerInvariant();

        return normalized is "busy" or "retry" or "running";
    }

    public static string DeriveKanbanStatus(
        string runtimeType,
        DateTimeOffset modifiedAt,
        bool? hasAssistantResponse,
        int recentWindowMs)
    {
        if (IsRuntimeRunning(runtimeType))
            return "in_progress";

        if (hasAssistantResponse == true)
            return "completed";

        if (hasAssistantResponse == false)
            return "pending";

        var age = DateTimeOffset.UtcNow - modifiedAt;

        return age.TotalMilliseconds <= recentWindowMs ? "pending" : "completed";
    }
}
