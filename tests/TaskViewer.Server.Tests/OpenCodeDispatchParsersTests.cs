using System.Text.Json.Nodes;
using TaskViewer.OpenCode;

namespace TaskViewer.Server.Tests;

public sealed class OpenCodeDispatchParsersTests
{
    [Fact]
    public void ParseCreatedSessionId_ReturnsTrimmedId()
    {
        var created = new JsonObject
        {
            ["id"] = " sess-123 "
        };

        var sessionId = OpenCodeDispatchParsers.ParseCreatedSessionId(created);

        Assert.Equal("sess-123", sessionId);
    }

    [Fact]
    public void ParseCreatedSessionId_ReturnsNullForMissingOrBlankId()
    {
        var missing = OpenCodeDispatchParsers.ParseCreatedSessionId(new JsonObject());
        var blank = OpenCodeDispatchParsers.ParseCreatedSessionId(
            new JsonObject
            {
                ["id"] = "   "
            });

        Assert.Null(missing);
        Assert.Null(blank);
    }
}
