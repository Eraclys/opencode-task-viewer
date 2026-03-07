namespace TaskViewer.OpenCode;

public sealed class DisabledOpenCodeStatusReader : IOpenCodeStatusReader
{
    public async Task<Dictionary<string, string>> ReadWorkingStatusMapAsync(string directory)
    {
        await Task.CompletedTask;
        return new Dictionary<string, string>(StringComparer.Ordinal);
    }
}
