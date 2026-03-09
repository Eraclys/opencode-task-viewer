namespace TaskViewer;

sealed class TimestampedValue<T>
{
    public TimestampedValue(DateTimeOffset timestamp, T value)
    {
        Timestamp = timestamp;
        Value = value;
    }

    public DateTimeOffset Timestamp { get; set; }
    public T Value { get; set; }
}