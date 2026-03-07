using System.Text.Json.Nodes;

namespace TaskViewer.OpenCode;

public sealed class DelegateOpenCodeStatusReader : IOpenCodeStatusReader
{
    readonly Func<string, OpenCodeRequest, Task<JsonNode?>> _openCodeFetch;

    public DelegateOpenCodeStatusReader(Func<string, OpenCodeRequest, Task<JsonNode?>> openCodeFetch)
    {
        _openCodeFetch = openCodeFetch;
    }

    public async Task<Dictionary<string, string>> ReadWorkingStatusMapAsync(string directory)
    {
        var data = await _openCodeFetch(
            "/session/status",
            new OpenCodeRequest
            {
                Directory = directory
            });

        return OpenCodeStatusParsers.ParseWorkingStatusMap(data);
    }
}
