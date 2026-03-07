using System.Text.Json.Nodes;
using TaskViewer.Server;

namespace TaskViewer.Server.Application.Orchestration;

public sealed class QueueDispatchService : IQueueDispatchService
{
    private readonly Func<string, OpenCodeRequest, Task<JsonNode?>> _openCodeFetch;
    private readonly Func<string, string?, string?> _buildOpenCodeSessionUrl;

    public QueueDispatchService(
        Func<string, OpenCodeRequest, Task<JsonNode?>> openCodeFetch,
        Func<string, string?, string?> buildOpenCodeSessionUrl)
    {
        _openCodeFetch = openCodeFetch;
        _buildOpenCodeSessionUrl = buildOpenCodeSessionUrl;
    }

    public async Task<QueueDispatchResult> DispatchAsync(QueueItemRecord item)
    {
        var title = $"[{item.IssueType ?? "ISSUE"}] {item.IssueKey}";
        var created = await _openCodeFetch(
            "/session",
            new OpenCodeRequest
            {
                Method = "POST",
                Directory = item.Directory,
                JsonBody = new JsonObject
                {
                    ["title"] = title
                }
            });

        var sessionId = created?["id"]?.ToString()?.Trim();

        if (string.IsNullOrWhiteSpace(sessionId))
            throw new InvalidOperationException("OpenCode did not return a session id");

        var prompt = ComposePrompt(item);

        await _openCodeFetch(
            $"/session/{Uri.EscapeDataString(sessionId)}/prompt_async",
            new OpenCodeRequest
            {
                Method = "POST",
                Directory = item.Directory,
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

        var openCodeUrl = _buildOpenCodeSessionUrl(sessionId, item.Directory);

        return new QueueDispatchResult(sessionId, openCodeUrl);
    }

    private static string ComposePrompt(QueueItemRecord item)
    {
        var lines = new List<string>
        {
            "Resolve the following SonarQube warning with a minimal, targeted change.",
            string.Empty,
            $"Issue key: {item.IssueKey}"
        };

        if (!string.IsNullOrWhiteSpace(item.IssueType))
            lines.Add($"Issue type: {item.IssueType}");

        if (!string.IsNullOrWhiteSpace(item.Severity))
            lines.Add($"Severity: {item.Severity}");

        if (!string.IsNullOrWhiteSpace(item.Rule))
            lines.Add($"Rule: {item.Rule}");

        if (!string.IsNullOrWhiteSpace(item.IssueStatus))
            lines.Add($"Issue status: {item.IssueStatus}");

        if (!string.IsNullOrWhiteSpace(item.RelativePath))
            lines.Add($"File: {item.RelativePath}");

        if (item.Line.HasValue)
            lines.Add($"Line: {item.Line.Value}");

        if (!string.IsNullOrWhiteSpace(item.Message))
            lines.Add($"Message: {item.Message}");

        lines.Add(string.Empty);
        lines.Add("Constraints:");
        lines.Add("- Fix only this issue; avoid unrelated refactors.");
        lines.Add("- Preserve behavior and public contracts.");
        lines.Add("- If the issue is not actionable, explain why and propose the safest alternative.");

        var extra = (item.Instructions ?? string.Empty).Trim();

        if (!string.IsNullOrWhiteSpace(extra))
        {
            lines.Add(string.Empty);
            lines.Add("Additional instructions:");
            lines.Add(extra);
        }

        return string.Join('\n', lines);
    }
}
