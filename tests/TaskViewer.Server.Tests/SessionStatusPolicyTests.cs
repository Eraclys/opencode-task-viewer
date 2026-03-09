using TaskViewer.Domain;

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
}
