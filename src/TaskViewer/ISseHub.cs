namespace TaskViewer;

public interface ISseHub
{
    Task Broadcast<T>(T data);
}
