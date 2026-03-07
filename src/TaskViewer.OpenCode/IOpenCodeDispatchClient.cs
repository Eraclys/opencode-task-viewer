namespace TaskViewer.OpenCode;

public interface IOpenCodeDispatchClient
{
    Task<string> CreateSessionAsync(string directory, string title);

    Task SendPromptAsync(string directory, string sessionId, string prompt);
}
