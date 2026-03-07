using System.Text.Json.Nodes;
using TaskViewer.Server;
using TaskViewer.Server.Application.Orchestration;

namespace TaskViewer.Server.Tests;

public sealed class OrchestrationUseCasesTests
{
    [Fact]
    public async Task GetInstructionProfileAsync_UppercasesIssueTypeAndParsesMappingId()
    {
        var gateway = new FakeGateway
        {
            InstructionProfileResult = new JsonObject { ["instructions"] = "Apply safe fix" }
        };

        var sut = new OrchestrationUseCases(gateway);
        var result = await sut.GetInstructionProfileAsync("42", "code_smell");

        Assert.Equal("42", result.GetType().GetProperty("mappingId")?.GetValue(result)?.ToString());
        Assert.Equal("CODE_SMELL", result.GetType().GetProperty("issueType")?.GetValue(result)?.ToString());
        Assert.Equal("Apply safe fix", result.GetType().GetProperty("instructions")?.GetValue(result)?.ToString());
    }

    [Fact]
    public async Task EnqueueAllMatchingAsync_UsesFallbackRuleField()
    {
        var gateway = new FakeGateway();
        var sut = new OrchestrationUseCases(gateway);

        await sut.EnqueueAllMatchingAsync(
            new JsonObject
            {
                ["mappingId"] = "9",
                ["issueType"] = "CODE_SMELL",
                ["rules"] = "javascript:S3776"
            });

        Assert.Equal("javascript:S3776", gateway.LastRuleKeys?.ToString());
    }

    [Fact]
    public async Task GetQueueAsync_ReturnsItemsStatsAndWorker()
    {
        var gateway = new FakeGateway
        {
            QueueItemsResult =
            [
                new QueueItemRecord { Id = 1, IssueKey = "sq-1", MappingId = 1, SonarProjectKey = "k", Directory = "C:/Work", CreatedAt = "2026-01-01T00:00:00.0000000+00:00", UpdatedAt = "2026-01-01T00:00:00.0000000+00:00" }
            ],
            QueueStatsResult = new { queued = 1 },
            WorkerStateResult = new { paused = false }
        };

        var sut = new OrchestrationUseCases(gateway);
        var result = await sut.GetQueueAsync("queued", "100");

        Assert.NotNull(result.GetType().GetProperty("items")?.GetValue(result));
        Assert.NotNull(result.GetType().GetProperty("stats")?.GetValue(result));
        Assert.NotNull(result.GetType().GetProperty("worker")?.GetValue(result));
    }

    private sealed class FakeGateway : IOrchestrationGateway
    {
        public object? LastRuleKeys { get; private set; }

        public JsonObject? InstructionProfileResult { get; set; }
        public List<QueueItemRecord> QueueItemsResult { get; set; } = [];
        public object QueueStatsResult { get; set; } = new { queued = 0 };
        public object WorkerStateResult { get; set; } = new { paused = false };

        public object GetPublicConfig() => new { configured = true };
        public Task<List<MappingRecord>> ListMappings() => Task.FromResult(new List<MappingRecord>());
        public Task<MappingRecord> UpsertMapping(JsonNode? payload) => Task.FromResult(new MappingRecord { Id = 1, SonarProjectKey = "key", Directory = "C:/Work", CreatedAt = "", UpdatedAt = "" });
        public Task<JsonObject?> GetInstructionProfile(object? mappingId, string? issueType) => Task.FromResult(InstructionProfileResult);
        public Task<JsonObject> UpsertInstructionProfile(object? mappingId, string? issueType, string? instructions)
            => Task.FromResult(new JsonObject { ["mapping_id"] = 1, ["issue_type"] = issueType, ["instructions"] = instructions, ["updated_at"] = "now" });
        public Task<object> ListIssues(object? mappingId, string? issueType, string? severity, string? issueStatus, object? page, object? pageSize, object? ruleKeys)
            => Task.FromResult<object>(new { total = 0, issues = Array.Empty<object>() });
        public Task<object> ListRules(object? mappingId, string? issueType, string? issueStatus)
            => Task.FromResult<object>(new { items = Array.Empty<object>() });
        public Task<object> EnqueueIssues(object? mappingId, string? issueType, string? instructions, JsonArray? issues)
            => Task.FromResult<object>(new { queued = 0 });

        public Task<object> EnqueueAllMatching(object? mappingId, string? issueType, object? ruleKeys, string? issueStatus, string? severity, string? instructions)
        {
            LastRuleKeys = ruleKeys;
            return Task.FromResult<object>(new { queued = 0 });
        }

        public Task<List<QueueItemRecord>> ListQueue(object? states, object? limit) => Task.FromResult(QueueItemsResult);
        public Task<object> GetQueueStats() => Task.FromResult(QueueStatsResult);
        public Task<object> GetWorkerState() => Task.FromResult(WorkerStateResult);
        public Task<bool> CancelQueueItem(object? queueId) => Task.FromResult(true);
        public Task<int> RetryFailed() => Task.FromResult(0);
        public Task<int> ClearQueued() => Task.FromResult(0);
    }
}
