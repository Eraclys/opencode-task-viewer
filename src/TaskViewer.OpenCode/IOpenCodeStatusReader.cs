namespace TaskViewer.OpenCode;

public interface IOpenCodeStatusReader
{
    Task<Dictionary<string, string>> ReadWorkingStatusMapAsync(string directory);
}
