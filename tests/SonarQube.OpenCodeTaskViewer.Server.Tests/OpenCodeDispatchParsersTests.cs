using OpenCode.Client;

namespace SonarQube.OpenCodeTaskViewer.Server.Tests;

public sealed class OpenCodeDispatchParsersTests
{
    [Fact]
    public void ParseCreatedSessionId_ReturnsTrimmedId()
    {
        var created = """{ "id": " sess-123 " }""";

        var sessionId = OpenCodeDispatchParsers.ParseCreatedSessionId(created);

        Assert.Equal("sess-123", sessionId);
    }

    [Fact]
    public void ParseCreatedSessionId_ReturnsNullForMissingOrBlankId()
    {
        var missing = OpenCodeDispatchParsers.ParseCreatedSessionId("{}");

        var blank = OpenCodeDispatchParsers.ParseCreatedSessionId(
            """{ "id": "   " }""");

        Assert.Null(missing);
        Assert.Null(blank);
    }
}
