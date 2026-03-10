namespace TaskViewer.Domain.Sessions;

public readonly record struct SessionRuntimeStatus
{
    SessionRuntimeStatus(string type, bool alreadyNormalized)
    {
        Type = alreadyNormalized ? type : Normalize(type);
    }

    public string Type { get; }

    public bool IsRunning => Type is "busy" or "retry" or "running" or "working";

    public static SessionRuntimeStatus Idle { get; } = new("idle", true);

    public static SessionRuntimeStatus FromRaw(string? type)
    {
        var normalized = Normalize(type);
        return string.IsNullOrWhiteSpace(normalized)
            ? Idle
            : new SessionRuntimeStatus(normalized, true);
    }

    public override string ToString() => Type;

    static string Normalize(string? type) => (type ?? string.Empty).Trim().ToLowerInvariant();
}
