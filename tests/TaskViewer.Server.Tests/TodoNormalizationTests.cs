using System.Text.Json.Nodes;
using TaskViewer.Server.Application;

namespace TaskViewer.Server.Tests;

public sealed class TodoNormalizationTests
{
    [Theory]
    [InlineData(null, "pending")]
    [InlineData("inprogress", "in_progress")]
    [InlineData("IN_PROGRESS", "in_progress")]
    [InlineData("done", "completed")]
    [InlineData("cancelled", "cancelled")]
    public void NormalizeStatus_MapsKnownValues(string? input, string expected)
    {
        Assert.Equal(expected, TodoNormalization.NormalizeStatus(input));
    }

    [Theory]
    [InlineData("p0", "high")]
    [InlineData("1", "high")]
    [InlineData("p2", "medium")]
    [InlineData("p3", "low")]
    [InlineData("medium", "medium")]
    [InlineData(null, null)]
    public void NormalizePriority_MapsKnownValues(string? input, string? expected)
    {
        Assert.Equal(expected, TodoNormalization.NormalizePriority(input));
    }

    [Fact]
    public void NormalizeTodo_MapsContentStatusAndPriority()
    {
        var input = new JsonObject
        {
            ["title"] = "Fix issue",
            ["state"] = "inprogress",
            ["priority"] = "p1"
        };

        var normalized = TodoNormalization.NormalizeTodo(input);

        Assert.Equal("Fix issue", normalized["content"]?.ToString());
        Assert.Equal("in_progress", normalized["status"]?.ToString());
        Assert.Equal("high", normalized["priority"]?.ToString());
    }
}
