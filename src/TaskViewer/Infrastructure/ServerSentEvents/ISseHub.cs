namespace TaskViewer.Infrastructure.ServerSentEvents;

public interface ISseHub
{
    Task Broadcast<T>(T data);
}
