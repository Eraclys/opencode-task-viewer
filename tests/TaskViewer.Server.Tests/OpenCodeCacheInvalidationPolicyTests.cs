using TaskViewer.Domain.Sessions;
using TaskViewer.Infrastructure.OpenCode;

namespace TaskViewer.Server.Tests;

public sealed class OpenCodeCacheInvalidationPolicyTests
{
    readonly OpenCodeCacheInvalidationPolicy _sut = new();

    [Fact]
    public void Decide_TodoUpdatedWithSession_OnlyInvalidatesSessionTodos()
    {
        var result = _sut.Decide(new OpenCodeEventEnvelope("C:/Work", "todo.updated", "sess-1", null));

        Assert.True(result.InvalidateSessionTodos);
        Assert.False(result.InvalidateAllCaches);
        Assert.True(result.BroadcastUpdate);
        Assert.Equal("sess-1", result.BroadcastSessionId);
    }

    [Fact]
    public void Decide_SessionStatusWithStatus_RecordsOverrideAndRefreshesLists()
    {
        var result = _sut.Decide(new OpenCodeEventEnvelope("C:/Work", "session.status", "sess-1", SessionRuntimeStatus.FromRaw("working")));

        Assert.False(result.InvalidateAllCaches);
        Assert.True(result.InvalidateSessionsList);
        Assert.True(result.InvalidateTaskOverview);
        Assert.Equal("C:/Work", result.StatusDirectory);
        Assert.Equal(SessionRuntimeStatus.FromRaw("working"), result.StatusType);
        Assert.Equal("sess-1", result.BroadcastSessionId);
    }

    [Fact]
    public void Decide_MessageEvent_ClearsAssistantPresenceAndRefreshesSessions()
    {
        var result = _sut.Decide(new OpenCodeEventEnvelope(null, "message.updated", null, null));

        Assert.True(result.ClearAssistantPresence);
        Assert.True(result.InvalidateSessionsList);
        Assert.False(result.InvalidateAllCaches);
        Assert.True(result.BroadcastUpdate);
    }

    [Fact]
    public void Decide_UnknownEvent_ReturnsNone()
    {
        var result = _sut.Decide(new OpenCodeEventEnvelope(null, "noop", null, null));

        Assert.Equal(OpenCodeCacheInvalidationDecision.None, result);
    }
}
