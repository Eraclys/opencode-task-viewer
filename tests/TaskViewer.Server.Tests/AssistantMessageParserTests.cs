using System.Text.Json.Nodes;
using TaskViewer.Server.Application;

namespace TaskViewer.Server.Tests;

public sealed class AssistantMessageParserTests
{
    [Fact]
    public void GetMessageRole_ExtractsFromInfoRole()
    {
        var message = new JsonObject
        {
            ["info"] = new JsonObject { ["role"] = "Assistant" }
        };

        Assert.Equal("assistant", AssistantMessageParser.GetMessageRole(message));
    }

    [Fact]
    public void ExtractAssistantMessageText_ReadsNestedPartsText()
    {
        var message = new JsonObject
        {
            ["parts"] = new JsonArray
            {
                new JsonObject { ["type"] = "text", ["text"] = "Line one" },
                new JsonObject { ["type"] = "text", ["text"] = "Line two" }
            }
        };

        var text = AssistantMessageParser.ExtractAssistantMessageText(message);

        Assert.Contains("Line one", text);
    }

    [Fact]
    public void ExtractMessageCreatedAt_NormalizesIsoTimestamp()
    {
        var message = new JsonObject
        {
            ["timestamp"] = "2026-01-01T12:30:00+02:00"
        };

        var createdAt = AssistantMessageParser.ExtractMessageCreatedAt(message);

        Assert.Equal("2026-01-01T10:30:00.0000000+00:00", createdAt);
    }
}
