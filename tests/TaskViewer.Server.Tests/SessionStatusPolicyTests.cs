using TaskViewer.Server.Domain;

namespace TaskViewer.Server.Tests;

public sealed class SessionStatusPolicyTests
{
    [Theory]
    [InlineData("busy", true)]
    [InlineData("retry", true)]
    [InlineData("running", true)]
    [InlineData("idle", false)]
    [InlineData("", false)]
    public void IsRuntimeRunning_MapsKnownValues(string runtimeType, bool expected) => Assert.Equal(expected, SessionStatusPolicy.IsRuntimeRunning(runtimeType));

    [Fact]
    public void DeriveKanbanStatus_ReturnsInProgress_WhenRuntimeIsRunning()
    {
        var status = SessionStatusPolicy.DeriveKanbanStatus(
            "busy",
            DateTimeOffset.UtcNow.ToString("O"),
            null,
            300_000);

        Assert.Equal("in_progress", status);
    }

    [Fact]
    public void DeriveKanbanStatus_UsesAssistantSignal_WhenRuntimeNotRunning()
    {
        var now = DateTimeOffset.UtcNow.ToString("O");

        Assert.Equal(
            "completed",
            SessionStatusPolicy.DeriveKanbanStatus(
                "idle",
                now,
                true,
                300_000));

        Assert.Equal(
            "pending",
            SessionStatusPolicy.DeriveKanbanStatus(
                "idle",
                now,
                false,
                300_000));
    }

    [Fact]
    public void DeriveKanbanStatus_FallsBackToTimestampWindow_WhenAssistantUnknown()
    {
        var recent = DateTimeOffset.UtcNow.AddMinutes(-1).ToString("O");
        var stale = DateTimeOffset.UtcNow.AddMinutes(-30).ToString("O");

        Assert.Equal(
            "pending",
            SessionStatusPolicy.DeriveKanbanStatus(
                "idle",
                recent,
                null,
                300_000));

        Assert.Equal(
            "completed",
            SessionStatusPolicy.DeriveKanbanStatus(
                "idle",
                stale,
                null,
                300_000));
    }
}
