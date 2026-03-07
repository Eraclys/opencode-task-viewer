using TaskViewer.OpenCode;

namespace TaskViewer.Server.Application.Orchestration;

public sealed class QueueDispatchService : IQueueDispatchService
{
    readonly IOpenCodeDispatchClient _openCodeDispatchClient;
    readonly Func<string, string?, string?> _buildOpenCodeSessionUrl;

    public QueueDispatchService(
        IOpenCodeDispatchClient openCodeDispatchClient,
        Func<string, string?, string?> buildOpenCodeSessionUrl)
    {
        _openCodeDispatchClient = openCodeDispatchClient;
        _buildOpenCodeSessionUrl = buildOpenCodeSessionUrl;
    }

    public async Task<QueueDispatchResult> DispatchAsync(QueueItemRecord item)
    {
        var title = $"[{item.IssueType ?? "ISSUE"}] {item.IssueKey}";

        var sessionId = await _openCodeDispatchClient.CreateSessionAsync(item.Directory, title);

        var prompt = ComposePrompt(item);

        await _openCodeDispatchClient.SendPromptAsync(item.Directory, sessionId, prompt);

        var openCodeUrl = _buildOpenCodeSessionUrl(sessionId, item.Directory);

        return new QueueDispatchResult(sessionId, openCodeUrl);
    }

    static string ComposePrompt(QueueItemRecord item)
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
