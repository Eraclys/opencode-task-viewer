using System.Text.Json.Nodes;
using TaskViewer.Application.Orchestration;
using TaskViewer.SonarQube;

namespace TaskViewer.Infrastructure.Orchestration;

public static class OrchestrationRequestParsers
{
    public static UpsertMappingRequest ParseUpsertMapping(JsonNode? payload)
    {
        return new UpsertMappingRequest(
            ParseNullableInt(payload?["id"]?.ToString()),
            payload?["sonarProjectKey"]?.ToString()?.Trim() ?? payload?["sonar_project_key"]?.ToString()?.Trim(),
            payload?["directory"]?.ToString()?.Trim(),
            NormalizeOptionalString(payload?["branch"]?.ToString()),
            payload?["enabled"] is null || payload?["enabled"]?.GetValue<bool>() != false);
    }

    public static UpsertInstructionProfileRequest ParseUpsertInstructionProfile(JsonNode? payload)
    {
        return new UpsertInstructionProfileRequest(
            ParseNullableInt(payload?["mappingId"]?.ToString()),
            payload?["issueType"]?.ToString(),
            payload?["instructions"]?.ToString());
    }

    public static EnqueueIssuesRequest ParseEnqueueIssues(JsonNode? payload)
    {
        return new EnqueueIssuesRequest(
            ParseNullableInt(payload?["mappingId"]?.ToString()),
            payload?["issueType"]?.ToString(),
            payload?["instructions"]?.ToString(),
            ParseIssues(payload?["issues"] as JsonArray));
    }

    public static EnqueueAllRequest ParseEnqueueAll(JsonNode? payload)
    {
        var ruleKeys = payload?["ruleKeys"] ?? payload?["rules"] ?? payload?["rule"];

        return new EnqueueAllRequest(
            ParseNullableInt(payload?["mappingId"]?.ToString()),
            payload?["issueType"]?.ToString(),
            ruleKeys?.ToString(),
            payload?["issueStatus"]?.ToString(),
            payload?["severity"]?.ToString(),
            payload?["instructions"]?.ToString());
    }

    public static TaskReviewRequestDto ParseTaskReviewRequest(JsonNode? payload)
    {
        return new TaskReviewRequestDto
        {
            Instructions = NormalizeOptionalString(payload?["instructions"]?.ToString()),
            Reason = NormalizeOptionalString(payload?["reason"]?.ToString())
        };
    }

    static int? ParseNullableInt(string? value)
        => int.TryParse(value, out var parsed) ? parsed : null;

    static IReadOnlyList<SonarIssueTransport>? ParseIssues(JsonArray? issues)
    {
        if (issues is null)
            return null;

        return issues
            .Select(SonarResponseParsers.ParseIssue)
            .OfType<SonarIssueTransport>()
            .ToList();
    }

    static string? NormalizeOptionalString(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }
}
