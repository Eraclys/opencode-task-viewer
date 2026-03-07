namespace TaskViewer.Server.Application.Orchestration;

public interface IQueueDispatchService
{
    Task<QueueDispatchResult> DispatchAsync(QueueItemRecord item);
}

public sealed record QueueDispatchResult(
    string SessionId,
    string? OpenCodeUrl);
