using System.Text.Json;
using System.Text.Json.Serialization;
using SonarQube.Client;
using SonarQube.OpenCodeTaskViewer.Domain.Orchestration;

namespace SonarQube.OpenCodeTaskViewer.Infrastructure.Orchestration;

public static class OrchestrationRequestParsers
{
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    public static UpsertMappingRequest ParseUpsertMapping(string? payload)
        => ParseUpsertMapping(Deserialize<UpsertMappingPayload>(payload));

    public static UpsertMappingRequest ParseUpsertMapping(UpsertMappingPayload? request)
    {
        return new UpsertMappingRequest(
            request?.Id,
            NormalizeOptionalString(request?.SonarProjectKey) ?? NormalizeOptionalString(request?.LegacySonarProjectKey),
            NormalizeOptionalString(request?.Directory),
            NormalizeOptionalString(request?.Branch),
            request?.Enabled != false);
    }

    public static UpsertInstructionProfileRequest ParseUpsertInstructionProfile(string? payload)
        => ParseUpsertInstructionProfile(Deserialize<UpsertInstructionProfilePayload>(payload));

    public static UpsertInstructionProfileRequest ParseUpsertInstructionProfile(UpsertInstructionProfilePayload? request)
    {
        return new UpsertInstructionProfileRequest(
            request?.MappingId,
            SonarIssueType.FromRaw(request?.IssueType),
            request?.Instructions);
    }

    public static EnqueueIssuesRequest ParseEnqueueIssues(string? payload)
        => ParseEnqueueIssues(Deserialize<EnqueueIssuesPayload>(payload));

    public static EnqueueIssuesRequest ParseEnqueueIssues(EnqueueIssuesPayload? request)
    {
        return new EnqueueIssuesRequest(
            request?.MappingId,
            SonarIssueType.FromRaw(request?.IssueType),
            request?.Instructions,
            request?.Issues?.Select(ParseIssue).OfType<SonarIssueTransport>().ToList());
    }

    public static EnqueueAllRequest ParseEnqueueAll(string? payload)
        => ParseEnqueueAll(Deserialize<EnqueueAllPayload>(payload));

    public static EnqueueAllRequest ParseEnqueueAll(EnqueueAllPayload? request)
    {
        var ruleKeys = NormalizeOptionalString(request?.RuleKeys) ?? NormalizeOptionalString(request?.Rules) ?? NormalizeOptionalString(request?.Rule);

        return new EnqueueAllRequest(
            request?.MappingId,
            SonarIssueType.FromRaw(request?.IssueType),
            ruleKeys,
            SonarIssueStatus.ParseCsv(request?.IssueStatus),
            SonarIssueSeverity.ParseCsv(request?.Severity),
            request?.Instructions);
    }

    public static TaskReviewRequestDto ParseTaskReviewRequest(TaskReviewPayload? request)
    {
        return new TaskReviewRequestDto
        {
            Instructions = NormalizeOptionalString(request?.Instructions),
            Reason = NormalizeOptionalString(request?.Reason)
        };
    }

    static T? Deserialize<T>(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
            return default;

        try
        {
            return JsonSerializer.Deserialize<T>(payload, JsonOptions);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    static string? NormalizeOptionalString(string? value)
    {
        var trimmed = value?.Trim();

        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    static SonarIssueTransport? ParseIssue(EnqueueIssuePayload? issue)
    {
        if (issue is null)
            return null;

        return new SonarIssueTransport(
            issue.Key,
            issue.IssueKey,
            issue.Type,
            issue.IssueType,
            issue.Severity,
            issue.Rule,
            issue.Message,
            issue.Component,
            issue.RelativePath ?? issue.AbsolutePath ?? issue.File,
            issue.Line?.ToString(),
            issue.Status);
    }

    public sealed class UpsertMappingPayload
    {
        [JsonPropertyName("id")] public int? Id { get; init; }

        [JsonPropertyName("sonarProjectKey")] public string? SonarProjectKey { get; init; }

        [JsonPropertyName("sonar_project_key")]
        public string? LegacySonarProjectKey { get; init; }

        [JsonPropertyName("directory")] public string? Directory { get; init; }

        [JsonPropertyName("branch")] public string? Branch { get; init; }

        [JsonPropertyName("enabled")] public bool? Enabled { get; init; }
    }

    public sealed class UpsertInstructionProfilePayload
    {
        [JsonPropertyName("mappingId")] public int? MappingId { get; init; }

        [JsonPropertyName("issueType")] public string? IssueType { get; init; }

        [JsonPropertyName("instructions")] public string? Instructions { get; init; }
    }

    public sealed class EnqueueIssuesPayload
    {
        [JsonPropertyName("mappingId")] public int? MappingId { get; init; }

        [JsonPropertyName("issueType")] public string? IssueType { get; init; }

        [JsonPropertyName("instructions")] public string? Instructions { get; init; }

        [JsonPropertyName("issues")] public List<EnqueueIssuePayload>? Issues { get; init; }
    }

    public sealed class EnqueueIssuePayload
    {
        [JsonPropertyName("key")] public string? Key { get; init; }

        [JsonPropertyName("issueKey")] public string? IssueKey { get; init; }

        [JsonPropertyName("type")] public string? Type { get; init; }

        [JsonPropertyName("issueType")] public string? IssueType { get; init; }

        [JsonPropertyName("severity")] public string? Severity { get; init; }

        [JsonPropertyName("rule")] public string? Rule { get; init; }

        [JsonPropertyName("message")] public string? Message { get; init; }

        [JsonPropertyName("component")] public string? Component { get; init; }

        [JsonPropertyName("file")] public string? File { get; init; }

        [JsonPropertyName("relativePath")] public string? RelativePath { get; init; }

        [JsonPropertyName("absolutePath")] public string? AbsolutePath { get; init; }

        [JsonPropertyName("line")] public JsonElement? Line { get; init; }

        [JsonPropertyName("status")] public string? Status { get; init; }
    }

    public sealed class EnqueueAllPayload
    {
        [JsonPropertyName("mappingId")] public int? MappingId { get; init; }

        [JsonPropertyName("issueType")] public string? IssueType { get; init; }

        [JsonPropertyName("ruleKeys")] public string? RuleKeys { get; init; }

        [JsonPropertyName("rules")] public string? Rules { get; init; }

        [JsonPropertyName("rule")] public string? Rule { get; init; }

        [JsonPropertyName("issueStatus")] public string? IssueStatus { get; init; }

        [JsonPropertyName("severity")] public string? Severity { get; init; }

        [JsonPropertyName("instructions")] public string? Instructions { get; init; }
    }

    public sealed class TaskReviewPayload
    {
        [JsonPropertyName("instructions")] public string? Instructions { get; init; }

        [JsonPropertyName("reason")] public string? Reason { get; init; }
    }
}
