namespace TaskViewer.Server.Domain;

public static class SessionStatusPolicy
{
    public static bool IsRuntimeRunning(string? type)
    {
        var normalized = (type ?? string.Empty).Trim().ToLowerInvariant();
        return normalized is "busy" or "retry" or "running";
    }

    public static string DeriveKanbanStatus(string runtimeType, string modifiedAt, bool? hasAssistantResponse, int recentWindowMs)
    {
        if (IsRuntimeRunning(runtimeType))
            return "in_progress";

        if (hasAssistantResponse == true)
            return "completed";

        if (hasAssistantResponse == false)
            return "pending";

        if (!DateTimeOffset.TryParse(modifiedAt, out var timestamp))
            return "pending";

        var age = DateTimeOffset.UtcNow - timestamp;
        return age.TotalMilliseconds <= recentWindowMs ? "pending" : "completed";
    }
}
