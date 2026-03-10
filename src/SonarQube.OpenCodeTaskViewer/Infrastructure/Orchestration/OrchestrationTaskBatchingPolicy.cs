using SonarQube.OpenCodeTaskViewer.Infrastructure.Persistence;

namespace SonarQube.OpenCodeTaskViewer.Infrastructure.Orchestration;

public static class OrchestrationTaskBatchingPolicy
{
    public const string TaskUnit = "project+file+rule";

    public static string BuildTaskKey(MappingRecord mapping, string? path, string? rule)
    {
        var normalizedPath = NormalizePath(path);
        var normalizedRule = NormalizeRule(rule);

        return $"{mapping.SonarProjectKey}::{mapping.Branch ?? string.Empty}::{normalizedPath}::{normalizedRule}";
    }

    public static string BuildLockKey(MappingRecord mapping, string? path) => $"{mapping.SonarProjectKey}::{mapping.Branch ?? string.Empty}::{NormalizePath(path)}";

    public static int ComputePriorityScore(IReadOnlyList<NormalizedIssue> issues, string? branch)
    {
        var severityScore = issues.Max(issue => issue.IssueSeverity.PriorityScore);
        var cheapFixBonus = issues.Count <= 3 ? 8 : 0;
        var branchBonus = string.IsNullOrWhiteSpace(branch) ? 0 : 2;

        return severityScore + cheapFixBonus + branchBonus;
    }

    public static string BuildRepresentativeMessage(IReadOnlyList<NormalizedIssue> issues, string? path, string? rule)
    {
        var representative = issues.FirstOrDefault()?.Message;

        if (!string.IsNullOrWhiteSpace(representative) &&
            issues.Count == 1)
            return representative;

        var normalizedPath = NormalizePath(path);
        var normalizedRule = NormalizeRule(rule);

        return $"Grouped {issues.Count} SonarQube warning(s) for {normalizedPath} [{normalizedRule}]";
    }

    public static string NormalizePath(string? path)
    {
        var normalized = (path ?? string.Empty).Trim().Replace('\\', '/');

        return string.IsNullOrWhiteSpace(normalized) ? "<unknown-file>" : normalized;
    }

    public static string NormalizeRule(string? rule)
    {
        var normalized = (rule ?? string.Empty).Trim();

        return string.IsNullOrWhiteSpace(normalized) ? "<unknown-rule>" : normalized;
    }
}
