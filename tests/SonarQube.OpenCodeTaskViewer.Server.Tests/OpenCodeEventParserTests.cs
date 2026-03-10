using OpenCode.Client;
using SonarQube.OpenCodeTaskViewer.Infrastructure.OpenCode;

namespace SonarQube.OpenCodeTaskViewer.Server.Tests;

public sealed class OpenCodeEventParserTests
{
    [Fact]
    public void Parse_ReturnsNull_WhenPayloadTypeMissing()
    {
        var result = OpenCodeEventParser.Parse(
            new OpenCodeSseEvent
            {
                Directory = "C:/Work",
                Payload = new OpenCodeSsePayload()
            });

        Assert.Null(result);
    }

    [Fact]
    public void Parse_NormalizesDirectory_AndReadsSessionIdVariants()
    {
        var result = OpenCodeEventParser.Parse(
            new OpenCodeSseEvent
            {
                Directory = "C:\\Work\\Repo\\",
                Payload = new OpenCodeSsePayload
                {
                    Type = "todo.updated",
                    Properties = new OpenCodeSseProperties
                    {
                        LegacySessionId = "sess-legacy"
                    }
                }
            });

        Assert.NotNull(result);
        Assert.Equal("C:\\Work\\Repo", result.Directory);
        Assert.Equal("todo.updated", result.Type);
        Assert.Equal("sess-legacy", result.SessionId);
    }

    [Fact]
    public void Parse_ReadsStatusTypeFromNestedStatusObject()
    {
        var result = OpenCodeEventParser.Parse(
            new OpenCodeSseEvent
            {
                Payload = new OpenCodeSsePayload
                {
                    Type = "session.status",
                    Properties = new OpenCodeSseProperties
                    {
                        SessionId = "sess-1",
                        Status = new OpenCodeSseStatus
                        {
                            Type = "busy"
                        }
                    }
                }
            });

        Assert.NotNull(result);
        Assert.Equal("sess-1", result.SessionId);
        Assert.Equal(SessionRuntimeStatus.FromRaw("busy"), result.StatusType);
    }

    [Fact]
    public void Parse_FallsBackToTopLevelTypeInProperties()
    {
        var result = OpenCodeEventParser.Parse(
            new OpenCodeSseEvent
            {
                Payload = new OpenCodeSsePayload
                {
                    Type = "session.status",
                    Properties = new OpenCodeSseProperties
                    {
                        SessionId = "sess-2",
                        Type = "idle"
                    }
                }
            });

        Assert.NotNull(result);
        Assert.Equal(SessionRuntimeStatus.FromRaw("idle"), result.StatusType);
    }
}
