namespace TaskViewer.Domain.Orchestration;

public readonly record struct TaskReviewAction
{
    TaskReviewAction(string value, bool alreadyNormalized)
    {
        Value = alreadyNormalized ? value : Normalize(value);
    }

    public string Value { get; }
    public bool HasValue => !string.IsNullOrWhiteSpace(Value);

    public static TaskReviewAction Approved { get; } = new("approved", true);
    public static TaskReviewAction Rejected { get; } = new("rejected", true);
    public static TaskReviewAction Requeue { get; } = new("requeue", true);
    public static TaskReviewAction Reprompt { get; } = new("reprompt", true);

    public static TaskReviewAction FromRaw(string? value)
    {
        var normalized = Normalize(value);

        return normalized switch
        {
            "approved" or "done" => Approved,
            "rejected" => Rejected,
            "requeue" or "requeued" => Requeue,
            "reprompt" or "reprompted" => Reprompt,
            _ => default
        };
    }

    public string? OrNull() => HasValue ? Value : null;

    public string? Or(string? fallback) => HasValue ? Value : fallback;

    public override string ToString() => Value;

    static string Normalize(string? value) => (value ?? string.Empty).Trim().ToLowerInvariant();
}
