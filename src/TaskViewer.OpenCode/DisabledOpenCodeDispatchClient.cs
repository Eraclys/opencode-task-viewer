namespace TaskViewer.OpenCode;

public sealed class DisabledOpenCodeDispatchClient : IOpenCodeDispatchClient
{
    public async Task<string> CreateSessionAsync(string directory, string title)
    {
        await Task.CompletedTask;
        throw new InvalidOperationException("OpenCode dispatch is not configured");
    }

    public async Task SendPromptAsync(string directory, string sessionId, string prompt)
    {
        await Task.CompletedTask;
        throw new InvalidOperationException("OpenCode dispatch is not configured");
    }
}
