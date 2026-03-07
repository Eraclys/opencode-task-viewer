using System.Text.Json.Nodes;

namespace TaskViewer.OpenCode.Tests;

public sealed class OpenCodeStatusParsersTests
{
    [Fact]
    public void ParseWorkingStatusMap_ReturnsNormalizedStatuses()
    {
        var payload = JsonNode.Parse(
            """
            {
              "session-a": { "type": " Working " },
              "session-b": { "type": "COMPLETED" },
              "session-c": { "ignored": true }
            }
            """);

        var statuses = OpenCodeStatusParsers.ParseWorkingStatusMap(payload);

        Assert.Equal(2, statuses.Count);
        Assert.Equal("working", statuses["session-a"]);
        Assert.Equal("completed", statuses["session-b"]);
    }

    [Fact]
    public void ParseWorkingStatusMap_ReturnsEmptyMapForScalarPayload()
    {
        var statuses = OpenCodeStatusParsers.ParseWorkingStatusMap(JsonValue.Create("unexpected"));

        Assert.Empty(statuses);
    }
}
