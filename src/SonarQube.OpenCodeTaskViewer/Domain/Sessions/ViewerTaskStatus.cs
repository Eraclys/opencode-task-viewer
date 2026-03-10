namespace SonarQube.OpenCodeTaskViewer.Domain.Sessions;

public readonly record struct ViewerTaskStatus
{
    public const string PendingValue = "pending";
    public const string InProgressValue = "in_progress";
    public const string CompletedValue = "completed";
    public const string CancelledValue = "cancelled";

    ViewerTaskStatus(string value, bool alreadyNormalized)
    {
        Value = alreadyNormalized ? value : Normalize(value);
    }

    public string Value { get; }

    public bool IsPending => Value == PendingValue;
    public bool IsInProgress => Value == InProgressValue;

    public static ViewerTaskStatus Pending { get; } = new(PendingValue, true);
    public static ViewerTaskStatus InProgress { get; } = new(InProgressValue, true);
    public static ViewerTaskStatus Completed { get; } = new(CompletedValue, true);
    public static ViewerTaskStatus Cancelled { get; } = new(CancelledValue, true);

    public static ViewerTaskStatus FromRaw(string? value)
    {
        var normalized = Normalize(value);

        return normalized switch
        {
            InProgressValue => InProgress,
            CompletedValue => Completed,
            CancelledValue => Cancelled,
            _ => Pending
        };
    }

    public override string ToString() => Value;

    static string Normalize(string? value) => (value ?? string.Empty).Trim().ToLowerInvariant();
}
