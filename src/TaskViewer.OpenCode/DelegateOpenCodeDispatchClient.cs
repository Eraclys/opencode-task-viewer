using System.Text.Json.Nodes;

namespace TaskViewer.OpenCode;

public sealed class DelegateOpenCodeDispatchClient : IOpenCodeDispatchClient
{
    readonly Func<string, OpenCodeRequest, Task<JsonNode?>> _openCodeFetch;

    public DelegateOpenCodeDispatchClient(Func<string, OpenCodeRequest, Task<JsonNode?>> openCodeFetch)
    {
        _openCodeFetch = openCodeFetch;
    }

    public async Task<string> CreateSessionAsync(string directory, string title)
    {
        var created = await _openCodeFetch(
            "/session",
            new OpenCodeRequest
            {
                Method = "POST",
                Directory = directory,
                JsonBody = new JsonObject
                {
                    ["title"] = title
                }
            });

        var sessionId = OpenCodeDispatchParsers.ParseCreatedSessionId(created);

        if (string.IsNullOrWhiteSpace(sessionId))
            throw new InvalidOperationException("OpenCode did not return a session id");

        return sessionId;
    }

    public async Task SendPromptAsync(string directory, string sessionId, string prompt)
    {
        await _openCodeFetch(
            $"/session/{Uri.EscapeDataString(sessionId)}/prompt_async",
            new OpenCodeRequest
            {
                Method = "POST",
                Directory = directory,
                JsonBody = new JsonObject
                {
                    ["parts"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["type"] = "text",
                            ["text"] = prompt
                        }
                    }
                }
            });
    }
}
