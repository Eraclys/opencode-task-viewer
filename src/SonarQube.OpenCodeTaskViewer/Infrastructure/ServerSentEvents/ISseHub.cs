namespace SonarQube.OpenCodeTaskViewer.Infrastructure.ServerSentEvents;

public interface ISseHub
{
    Task Broadcast<T>(T data);
}
