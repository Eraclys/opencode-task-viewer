namespace SonarQube.OpenCodeTaskViewer.Domain.Orchestration;

public readonly record struct QueueState
{
    public const string QueuedValue = "queued";
    public const string DispatchingValue = "dispatching";
    public const string LeasedValue = "leased";
    public const string RunningValue = "running";
    public const string AwaitingReviewValue = "awaiting_review";
    public const string RejectedValue = "rejected";
    public const string SessionCreatedValue = "session_created";
    public const string DoneValue = "done";
    public const string FailedValue = "failed";
    public const string CancelledValue = "cancelled";

    QueueState(string value, bool alreadyNormalized)
    {
        Value = alreadyNormalized ? value : Normalize(value);
    }

    public string Value { get; }

    public bool IsActive => Value is LeasedValue or RunningValue;
    public bool IsTerminal => Value is DoneValue or FailedValue or CancelledValue;
    public bool CanCancel => Value is QueuedValue or DispatchingValue or LeasedValue or RunningValue;
    public bool CanReprompt => Value is AwaitingReviewValue or RejectedValue;
    public bool CanReview => Value == AwaitingReviewValue;

    public string BoardStatus => Value switch
    {
        DispatchingValue or LeasedValue or RunningValue => "in_progress",
        AwaitingReviewValue => "completed",
        RejectedValue or FailedValue or CancelledValue => "cancelled",
        _ => "pending"
    };

    public string DisplayLabel => Value switch
    {
        DispatchingValue => "Dispatching",
        LeasedValue => "Leased",
        RunningValue => "Running",
        AwaitingReviewValue => "Review",
        RejectedValue => "Rejected",
        FailedValue => "Failed",
        CancelledValue => "Cancelled",
        DoneValue => "Done",
        SessionCreatedValue => "Session Created",
        _ => "Task"
    };

    public static QueueState Queued { get; } = new(QueuedValue, true);
    public static QueueState Dispatching { get; } = new(DispatchingValue, true);
    public static QueueState Leased { get; } = new(LeasedValue, true);
    public static QueueState Running { get; } = new(RunningValue, true);
    public static QueueState AwaitingReview { get; } = new(AwaitingReviewValue, true);
    public static QueueState Rejected { get; } = new(RejectedValue, true);
    public static QueueState SessionCreated { get; } = new(SessionCreatedValue, true);
    public static QueueState Done { get; } = new(DoneValue, true);
    public static QueueState Failed { get; } = new(FailedValue, true);
    public static QueueState Cancelled { get; } = new(CancelledValue, true);

    public static IReadOnlyList<QueueState> SessionVisibleStates { get; } =
    [
        Queued,
        Dispatching,
        Leased,
        Running,
        AwaitingReview,
        Rejected,
        Failed,
        Cancelled
    ];

    public static bool TryParse(string? value, out QueueState state)
    {
        switch (Normalize(value))
        {
            case QueuedValue:
                state = Queued;

                return true;
            case DispatchingValue:
                state = Dispatching;

                return true;
            case LeasedValue:
                state = Leased;

                return true;
            case RunningValue:
                state = Running;

                return true;
            case AwaitingReviewValue:
                state = AwaitingReview;

                return true;
            case RejectedValue:
                state = Rejected;

                return true;
            case SessionCreatedValue:
                state = SessionCreated;

                return true;
            case DoneValue:
                state = Done;

                return true;
            case FailedValue:
                state = Failed;

                return true;
            case CancelledValue:
                state = Cancelled;

                return true;
            default:
                state = default;

                return false;
        }
    }

    public static QueueState Parse(string? value)
    {
        if (TryParse(value, out var state))
            return state;

        throw new InvalidOperationException($"Unknown queue state '{value}'.");
    }

    public override string ToString() => Value;

    static string Normalize(string? value) => (value ?? string.Empty).Trim().ToLowerInvariant();
}
