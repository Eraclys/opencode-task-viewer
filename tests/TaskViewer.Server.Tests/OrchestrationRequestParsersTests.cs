using TaskViewer.Infrastructure.Orchestration;
using TaskViewer.SonarQube;

namespace TaskViewer.Server.Tests;

public sealed class OrchestrationRequestParsersTests
{
    [Fact]
    public void ParseUpsertMapping_SupportsLegacyProjectKey_AndOptionalFields()
    {
        var request = OrchestrationRequestParsers.ParseUpsertMapping(
            """
            {
              "id": "7",
              "sonar_project_key": "alpha-key",
              "directory": "C:/Work/Alpha",
              "branch": "  ",
              "enabled": false
            }
            """);

        Assert.Equal(7, request.Id);
        Assert.Equal("alpha-key", request.SonarProjectKey);
        Assert.Equal("C:/Work/Alpha", request.Directory);
        Assert.Null(request.Branch);
        Assert.False(request.Enabled);
    }

    [Fact]
    public void ParseUpsertInstructionProfile_ParsesMappingIdAndText()
    {
        var request = OrchestrationRequestParsers.ParseUpsertInstructionProfile(
            """
            {
              "mappingId": "42",
              "issueType": "code_smell",
              "instructions": "Do the safe change"
            }
            """);

        Assert.Equal(42, request.MappingId);
        Assert.Equal(SonarIssueType.CodeSmell, request.IssueType);
        Assert.Equal("Do the safe change", request.Instructions);
    }

    [Fact]
    public void ParseEnqueueIssues_ParsesIssueArray()
    {
        var request = OrchestrationRequestParsers.ParseEnqueueIssues(
            """
            {
              "mappingId": "5",
              "issueType": "BUG",
              "instructions": "fix it",
              "issues": [
                { "key": "sq-1" }
              ]
            }
            """);

        Assert.Equal(5, request.MappingId);
        Assert.Equal(SonarIssueType.Bug, request.IssueType);
        Assert.Equal("fix it", request.Instructions);
        Assert.Single(request.Issues!);
        Assert.Equal("sq-1", request.Issues![0].Key);
    }

    [Fact]
    public void ParseEnqueueAll_UsesRuleFallbacks()
    {
        var fromRules = OrchestrationRequestParsers.ParseEnqueueAll(
            """
            {
              "mappingId": "9",
              "rules": "javascript:S3776"
            }
            """);

        var fromRule = OrchestrationRequestParsers.ParseEnqueueAll(
            """
            {
              "mappingId": "9",
              "rule": "csharpsquid:S1118"
            }
            """);

        Assert.Equal("javascript:S3776", fromRules.RuleKeys);
        Assert.Equal("csharpsquid:S1118", fromRule.RuleKeys);
        Assert.Empty(fromRules.IssueStatuses);
        Assert.Empty(fromRules.Severities);
    }
}
