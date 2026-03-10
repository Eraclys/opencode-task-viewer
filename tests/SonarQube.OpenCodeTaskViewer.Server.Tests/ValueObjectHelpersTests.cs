using OpenCode.Client;
using SonarQube.Client;
using SonarQube.OpenCodeTaskViewer.Domain.Orchestration;
using SonarQube.OpenCodeTaskViewer.Domain.Sessions;

namespace SonarQube.OpenCodeTaskViewer.Server.Tests;

public sealed class ValueObjectHelpersTests
{
    [Fact]
    public void SonarIssueType_ToFilterList_ReturnsSingleton_WhenValueExists()
    {
        var type = SonarIssueType.FromRaw("code_smell");

        var values = type.ToFilterList();

        var only = Assert.Single(values);
        Assert.Equal(SonarIssueType.CodeSmell, only);
        Assert.Equal("CODE_SMELL", type.OrNull());
        Assert.Equal("CODE_SMELL", type.Or("BUG"));
    }

    [Fact]
    public void SonarIssueType_ToFilterList_ReturnsEmpty_WhenValueMissing()
    {
        var type = SonarIssueType.FromRaw(null);

        Assert.Empty(type.ToFilterList());
        Assert.Null(type.OrNull());
        Assert.Equal("BUG", type.Or("BUG"));
    }

    [Fact]
    public void SonarIssueStatus_ParseCsv_ParsesMultipleDistinctValues()
    {
        var values = SonarIssueStatus.ParseCsv("open, confirmed, OPEN, reopened");

        Assert.Equal(
            [
                SonarIssueStatus.Open,
                SonarIssueStatus.Confirmed,
                SonarIssueStatus.Reopened
            ],
            values);
    }

    [Fact]
    public void SonarIssueSeverity_ExposesPriorityAndFallbackHelpers()
    {
        var severity = SonarIssueSeverity.FromRaw("critical");

        Assert.True(severity.HasValue);
        Assert.Equal(45, severity.PriorityScore);
        Assert.Equal("CRITICAL", severity.OrNull());
        Assert.Equal(SonarIssueSeverity.Critical, Assert.Single(severity.ToFilterList()));
    }

    [Fact]
    public void SonarIssueStatus_ExposesFilterHelpers()
    {
        var status = SonarIssueStatus.FromRaw("open");

        Assert.True(status.HasValue);
        Assert.Equal("OPEN", status.OrNull());
        Assert.Equal(SonarIssueStatus.Open, Assert.Single(status.ToFilterList()));
    }

    [Theory]
    [InlineData("done", "approved")]
    [InlineData("approved", "approved")]
    [InlineData("requeued", "requeue")]
    [InlineData("reprompted", "reprompt")]
    public void TaskReviewAction_NormalizesAliases(string raw, string expected)
    {
        var action = TaskReviewAction.FromRaw(raw);

        Assert.Equal(expected, action.Value);
        Assert.Equal(expected, action.OrNull());
    }

    [Fact]
    public void QueueState_HelpersExposeExpectedSemantics()
    {
        Assert.True(QueueState.Running.IsActive);
        Assert.True(QueueState.Done.IsTerminal);
        Assert.Equal("in_progress", QueueState.Leased.BoardStatus);
        Assert.Equal("Review", QueueState.AwaitingReview.DisplayLabel);
    }

    [Fact]
    public void ViewerTaskStatus_DefaultsToPending_WhenUnknown()
    {
        var status = ViewerTaskStatus.FromRaw("unknown");

        Assert.Equal(ViewerTaskStatus.Pending, status);
        Assert.True(status.IsPending);
        Assert.False(status.IsInProgress);
    }

    [Theory]
    [InlineData("busy", true)]
    [InlineData("working", true)]
    [InlineData("idle", false)]
    public void SessionRuntimeStatus_CapturesRunningSemantics(string raw, bool expected)
    {
        var status = SessionRuntimeStatus.FromRaw(raw);

        Assert.Equal(expected, status.IsRunning);
    }
}
