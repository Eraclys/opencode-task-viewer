using TaskViewer.Domain.Orchestration;

namespace TaskViewer.Server.Tests;

public sealed class EnqueueContextPolicyTests
{
    [Fact]
    public void ResolveInstructionText_PrefersExplicitInstructions()
    {
        var result = EnqueueContextPolicy.ResolveInstructionText("  fix only this  ", "default");
        Assert.Equal("fix only this", result);
    }

    [Fact]
    public void ResolveInstructionText_FallsBackToDefaultInstructions()
    {
        var result = EnqueueContextPolicy.ResolveInstructionText("   ", "  default text  ");
        Assert.Equal("default text", result);
    }

    [Theory]
    [InlineData("CODE_SMELL", "text", true)]
    [InlineData(null, "text", false)]
    [InlineData("CODE_SMELL", "", false)]
    public void ShouldPersistInstructionProfile_RequiresTypeAndText(string? type, string text, bool expected) => Assert.Equal(expected, EnqueueContextPolicy.ShouldPersistInstructionProfile(type, text));
}
