using System.Text.Json.Nodes;
using TaskViewer.Server.Infrastructure.OpenCode;

namespace TaskViewer.Server.Tests;

public sealed class OpenCodeEventParserTests
{
    [Fact]
    public void Parse_ReturnsNull_WhenPayloadTypeMissing()
    {
        var result = OpenCodeEventParser.Parse(
            new JsonObject
            {
                ["directory"] = "C:/Work",
                ["payload"] = new JsonObject()
            });

        Assert.Null(result);
    }

    [Fact]
    public void Parse_NormalizesDirectory_AndReadsSessionIdVariants()
    {
        var result = OpenCodeEventParser.Parse(
            new JsonObject
            {
                ["directory"] = "C:\\Work\\Repo\\",
                ["payload"] = new JsonObject
                {
                    ["type"] = "todo.updated",
                    ["properties"] = new JsonObject
                    {
                        ["sessionID"] = "sess-legacy"
                    }
                }
            });

        Assert.NotNull(result);
        Assert.Equal("C:\\Work\\Repo", result!.Directory);
        Assert.Equal("todo.updated", result.Type);
        Assert.Equal("sess-legacy", result.SessionId);
    }

    [Fact]
    public void Parse_ReadsStatusTypeFromNestedStatusObject()
    {
        var result = OpenCodeEventParser.Parse(
            new JsonObject
            {
                ["payload"] = new JsonObject
                {
                    ["type"] = "session.status",
                    ["properties"] = new JsonObject
                    {
                        ["sessionId"] = "sess-1",
                        ["status"] = new JsonObject
                        {
                            ["type"] = "busy"
                        }
                    }
                }
            });

        Assert.NotNull(result);
        Assert.Equal("sess-1", result!.SessionId);
        Assert.Equal("busy", result.StatusType);
    }

    [Fact]
    public void Parse_FallsBackToTopLevelTypeInProperties()
    {
        var result = OpenCodeEventParser.Parse(
            new JsonObject
            {
                ["payload"] = new JsonObject
                {
                    ["type"] = "session.status",
                    ["properties"] = new JsonObject
                    {
                        ["sessionId"] = "sess-2",
                        ["type"] = "idle"
                    }
                }
            });

        Assert.NotNull(result);
        Assert.Equal("idle", result!.StatusType);
    }
}
