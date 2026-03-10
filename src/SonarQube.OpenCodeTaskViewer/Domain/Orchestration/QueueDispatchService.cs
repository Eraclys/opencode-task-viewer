using OpenCode.Client;
using SonarQube.OpenCodeTaskViewer.Infrastructure.Persistence;

namespace SonarQube.OpenCodeTaskViewer.Domain.Orchestration;

public sealed class QueueDispatchService : IQueueDispatchService
{
    readonly Func<string, string?, string?> _buildOpenCodeSessionUrl;
    readonly IOpenCodeService _openCodeService;

    public QueueDispatchService(
        IOpenCodeService openCodeService,
        Func<string, string?, string?> buildOpenCodeSessionUrl)
    {
        _openCodeService = openCodeService;
        _buildOpenCodeSessionUrl = buildOpenCodeSessionUrl;
    }

    public async Task<QueueDispatchResult> DispatchAsync(QueueItemRecord item, IReadOnlyList<NormalizedIssue> issues)
    {
        var title = BuildTitle(item);
        var sessionId = await _openCodeService.CreateSessionAsync(item.Directory, title);
        var prompt = ComposePrompt(item, issues);

        await _openCodeService.SendPromptAsync(item.Directory, sessionId, prompt);

        var openCodeUrl = _buildOpenCodeSessionUrl(sessionId, item.Directory);

        return new QueueDispatchResult(sessionId, openCodeUrl);
    }

    static string BuildTitle(QueueItemRecord item)
    {
        var rule = string.IsNullOrWhiteSpace(item.Rule) ? "RULE" : item.Rule;
        var path = string.IsNullOrWhiteSpace(item.RelativePath) ? item.AbsolutePath ?? item.IssueKey : item.RelativePath;
        var issueType = item.ParsedIssueType.Or("ISSUE") ?? "ISSUE";

        return $"[{issueType}] {rule} :: {path}";
    }

    static string ComposePrompt(QueueItemRecord item, IReadOnlyList<NormalizedIssue> issues)
    {
        var lines = new List<string>
        {
            $"Resolve the grouped SonarQube task using the `{item.TaskUnit ?? "project+file+rule"}` batching model.",
            string.Empty,
            $"Task key: {item.TaskKey ?? item.IssueKey}",
            $"Project: {item.SonarProjectKey}",
            $"Directory: {item.Directory}",
            $"Issue type: {item.ParsedIssueType.Or("UNKNOWN") ?? "UNKNOWN"}",
            $"Rule: {item.Rule ?? "UNKNOWN"}",
            $"Issue count: {Math.Max(1, item.IssueCount)}"
        };

        if (!string.IsNullOrWhiteSpace(item.RelativePath))
            lines.Add($"Primary file: {item.RelativePath}");

        if (!string.IsNullOrWhiteSpace(item.Message))
            lines.Add($"Task summary: {item.Message}");

        lines.Add(string.Empty);
        lines.Add("Goals:");
        lines.Add("- Make one coherent set of changes for this file+rule batch.");
        lines.Add("- Do not modify unrelated files unless absolutely required.");
        lines.Add("- If some linked warnings are stale or false positives, explain that clearly.");

        lines.Add(string.Empty);
        lines.Add("Linked warnings:");

        foreach (var issue in issues.OrderBy(issue => issue.Line ?? int.MaxValue).ThenBy(issue => issue.Key, StringComparer.Ordinal))
        {
            var location = issue.RelativePath ?? issue.AbsolutePath ?? "<unknown>";
            var lineSuffix = issue.Line.HasValue ? $":{issue.Line.Value}" : string.Empty;
            lines.Add($"- {issue.Key} | {issue.Rule ?? item.Rule ?? "UNKNOWN"} | {location}{lineSuffix}");

            if (!string.IsNullOrWhiteSpace(issue.Message))
                lines.Add($"  Message: {issue.Message}");
        }

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
