using TaskViewer.Domain.Orchestration;
using TaskViewer.Infrastructure.Orchestration;
using TaskViewer.Infrastructure.Persistence;

namespace TaskViewer.Server.Tests;

public sealed class OrchestrationUseCasesTests
{
    [Fact]
    public async Task GetInstructionProfileAsync_UppercasesIssueType()
    {
        var gateway = new FakeGateway
        {
            InstructionProfileResult = new InstructionProfileRecord
            {
                Id = 1,
                MappingId = 42,
                IssueType = "CODE_SMELL",
                Instructions = "Apply safe fix",
                CreatedAt = DateTimeOffset.UnixEpoch,
                UpdatedAt = DateTimeOffset.UnixEpoch
            }
        };

        var sut = new OrchestrationUseCases(gateway);
        var result = await sut.GetInstructionProfileAsync(42, "code_smell");

        Assert.Equal(42, result.MappingId);
        Assert.Equal("CODE_SMELL", result.IssueType);
        Assert.Equal("Apply safe fix", result.Instructions);
    }

    [Fact]
    public async Task DeleteMappingAsync_ForwardsMappingId()
    {
        var gateway = new FakeGateway();
        var sut = new OrchestrationUseCases(gateway);

        var deleted = await sut.DeleteMappingAsync(42);

        Assert.True(deleted);
        Assert.Equal(42, gateway.LastDeletedMappingId);
    }

    [Fact]
    public async Task EnqueueAllMatchingAsync_UsesFallbackRuleField()
    {
        var gateway = new FakeGateway();
        var sut = new OrchestrationUseCases(gateway);

        await sut.EnqueueAllMatchingAsync(
            new EnqueueAllRequest(
                MappingId: 9,
                IssueType: "CODE_SMELL",
                RuleKeys: "javascript:S3776",
                IssueStatus: null,
                Severity: null,
                Instructions: null));

        Assert.Equal("javascript:S3776", gateway.LastRuleKeys);
    }

    [Fact]
    public async Task GetQueueAsync_ReturnsItemsStatsAndWorker()
    {
        var gateway = new FakeGateway
        {
            QueueItemsResult =
            [
                new QueueItemRecord
                {
                    Id = 1,
                    IssueKey = "sq-1",
                    MappingId = 1,
                    SonarProjectKey = "k",
                    Directory = "C:/Work",
                    CreatedAt = DateTimeOffset.Parse("2026-01-01T00:00:00.0000000+00:00"),
                    UpdatedAt = DateTimeOffset.Parse("2026-01-01T00:00:00.0000000+00:00")
                }
            ],
            QueueStatsResult = new QueueStatsDto
            {
                Queued = 1,
                Dispatching = 0,
                SessionCreated = 0,
                Done = 0,
                Failed = 0,
                Cancelled = 0
            },
            WorkerStateResult = new OrchestrationWorkerStateDto
            {
                InFlightDispatches = 0,
                MaxActiveDispatches = 3,
                PausedByWorking = false,
                WorkingCount = 0,
                MaxWorkingGlobal = 5,
                WorkingResumeBelow = 4,
                WorkingSampleAt = null
            }
        };

        var sut = new OrchestrationUseCases(gateway);
        var result = await sut.GetQueueAsync("queued", 100);

        Assert.NotNull(result.Items);
        Assert.NotNull(result.Stats);
        Assert.NotNull(result.Worker);
    }

    [Fact]
    public async Task GetTaskReviewHistoryAsync_ForwardsTaskId()
    {
        var gateway = new FakeGateway
        {
            ReviewHistoryResult =
            [
                new TaskReviewHistoryDto
                {
                    Action = "rejected",
                    Reason = "Needs manual follow-up",
                    CreatedAt = DateTimeOffset.Parse("2026-03-08T10:04:00Z")
                }
            ]
        };

        var sut = new OrchestrationUseCases(gateway);
        var result = await sut.GetTaskReviewHistoryAsync(42);

        Assert.Single(result);
        Assert.Equal(42, gateway.LastReviewTaskId);
        Assert.Equal("rejected", result[0].Action);
    }

    [Fact]
    public async Task ReviewActions_ForwardReasonAndTaskId()
    {
        var gateway = new FakeGateway();
        var sut = new OrchestrationUseCases(gateway);

        await sut.ApproveTaskAsync(12);
        await sut.RejectTaskAsync(13, "Needs prompt changes");
        await sut.RequeueTaskAsync(14, "Retry later");

        Assert.Equal(12, gateway.LastApprovedTaskId);
        Assert.Equal(13, gateway.LastRejectedTaskId);
        Assert.Equal("Needs prompt changes", gateway.LastRejectedReason);
        Assert.Equal(14, gateway.LastRequeuedTaskId);
        Assert.Equal("Retry later", gateway.LastRequeuedReason);
    }

    [Fact]
    public async Task RepromptTaskAsync_ForwardsInstructionsAndReason()
    {
        var gateway = new FakeGateway();
        var sut = new OrchestrationUseCases(gateway);

        await sut.RepromptTaskAsync(22, "Retry with a narrower patch", "Previous response overreached");

        Assert.Equal(22, gateway.LastRepromptedTaskId);
        Assert.Equal("Retry with a narrower patch", gateway.LastRepromptedInstructions);
        Assert.Equal("Previous response overreached", gateway.LastRepromptedReason);
    }

    sealed class FakeGateway : IOrchestrationGateway
    {
        public string? LastRuleKeys { get; private set; }

        public InstructionProfileRecord? InstructionProfileResult { get; set; }
        public List<QueueItemRecord> QueueItemsResult { get; set; } = [];
        public IReadOnlyList<TaskReviewHistoryDto> ReviewHistoryResult { get; set; } = [];
        public int? LastDeletedMappingId { get; private set; }
        public int? LastReviewTaskId { get; private set; }
        public int? LastApprovedTaskId { get; private set; }
        public int? LastRejectedTaskId { get; private set; }
        public string? LastRejectedReason { get; private set; }
        public int? LastRequeuedTaskId { get; private set; }
        public string? LastRequeuedReason { get; private set; }
        public int? LastRepromptedTaskId { get; private set; }
        public string? LastRepromptedInstructions { get; private set; }
        public string? LastRepromptedReason { get; private set; }

        public QueueStatsDto QueueStatsResult { get; set; } = new()
        {
            Queued = 0,
            Dispatching = 0,
            SessionCreated = 0,
            Done = 0,
            Failed = 0,
            Cancelled = 0
        };

        public OrchestrationWorkerStateDto WorkerStateResult { get; set; } = new()
        {
            InFlightDispatches = 0,
            MaxActiveDispatches = 3,
            PausedByWorking = false,
            WorkingCount = 0,
            MaxWorkingGlobal = 5,
            WorkingResumeBelow = 4,
            WorkingSampleAt = null
        };

        public OrchestrationConfigDto GetPublicConfig() => new()
        {
            Configured = true,
            MaxActive = 3,
            PollMs = 3000,
            MaxAttempts = 3,
            MaxWorkingGlobal = 5,
            WorkingResumeBelow = 4
        };

        public Task<List<MappingRecord>> ListMappings(CancellationToken cancellationToken = default) => Task.FromResult(new List<MappingRecord>());

        public Task<bool> DeleteMapping(int mappingId, CancellationToken cancellationToken = default)
        {
            LastDeletedMappingId = mappingId;
            return Task.FromResult(mappingId > 0);
        }

        public Task<MappingRecord> UpsertMapping(UpsertMappingRequest request, CancellationToken cancellationToken = default) => Task.FromResult(
            new MappingRecord
            {
                Id = 1,
                SonarProjectKey = "key",
                Directory = "C:/Work",
                CreatedAt = DateTimeOffset.UnixEpoch,
                UpdatedAt = DateTimeOffset.UnixEpoch
            });

        public Task<InstructionProfileRecord?> GetInstructionProfile(int? mappingId, string? issueType, CancellationToken cancellationToken = default) => Task.FromResult(InstructionProfileResult);

        public Task<InstructionProfileRecord> UpsertInstructionProfile(UpsertInstructionProfileRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(
                new InstructionProfileRecord
                {
                    Id = 1,
                    MappingId = 1,
                    IssueType = request.IssueType ?? string.Empty,
                    Instructions = request.Instructions ?? string.Empty,
                    CreatedAt = DateTimeOffset.UnixEpoch,
                    UpdatedAt = DateTimeOffset.UnixEpoch
                });

        public Task<IssuesListDto> ListIssues(
            int mappingId,
            string? issueType,
            string? severity,
            string? issueStatus,
            int? page,
            int? pageSize,
            string? ruleKeys,
            CancellationToken cancellationToken = default)
            => Task.FromResult(
                new IssuesListDto
                {
                    Mapping = new MappingRecord
                    {
                        Id = 1,
                        SonarProjectKey = "k",
                        Directory = "C:/Work",
                        CreatedAt = DateTimeOffset.UnixEpoch,
                        UpdatedAt = DateTimeOffset.UnixEpoch
                    },
                    Paging = new IssuesPagingDto
                    {
                        PageIndex = 1,
                        PageSize = 100,
                        Total = 0
                    },
                    Issues = []
                });

        public Task<RulesListDto> ListRules(int mappingId, string? issueType, string? issueStatus, CancellationToken cancellationToken = default)
            => Task.FromResult(
                new RulesListDto
                {
                    Mapping = new MappingRecord
                    {
                        Id = 1,
                        SonarProjectKey = "k",
                        Directory = "C:/Work",
                        CreatedAt = DateTimeOffset.UnixEpoch,
                        UpdatedAt = DateTimeOffset.UnixEpoch
                    },
                    IssueType = issueType,
                    IssueStatus = issueStatus,
                    ScannedIssues = 0,
                    Truncated = false,
                    Rules = []
                });

        public Task<EnqueueIssuesResultDto> EnqueueIssues(EnqueueIssuesRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(
                new EnqueueIssuesResultDto
                {
                    Created = 0,
                    Skipped = [],
                    Items = []
                });

        public Task<EnqueueAllResultDto> EnqueueAllMatching(EnqueueAllRequest request, CancellationToken cancellationToken = default)
        {
            LastRuleKeys = request.RuleKeys;

            return Task.FromResult(
                new EnqueueAllResultDto
                {
                    Matched = 0,
                    Created = 0,
                    Skipped = [],
                    Truncated = false,
                    Items = []
                });
        }

        public Task<List<QueueItemRecord>> ListQueue(string? states, int? limit, CancellationToken cancellationToken = default) => Task.FromResult(QueueItemsResult);
        public Task<QueueStatsDto> GetQueueStats(CancellationToken cancellationToken = default) => Task.FromResult(QueueStatsResult);
        public Task<OrchestrationWorkerStateDto> GetWorkerState(CancellationToken cancellationToken = default) => Task.FromResult(WorkerStateResult);
        public Task<bool> CancelQueueItem(int queueId, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<int> RetryFailed(CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task<int> ClearQueued(CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task<bool> ApproveTask(int taskId, CancellationToken cancellationToken = default)
        {
            LastApprovedTaskId = taskId;
            return Task.FromResult(true);
        }

        public Task<bool> RejectTask(int taskId, string? reason, CancellationToken cancellationToken = default)
        {
            LastRejectedTaskId = taskId;
            LastRejectedReason = reason;
            return Task.FromResult(true);
        }

        public Task<bool> RequeueTask(int taskId, string? reason, CancellationToken cancellationToken = default)
        {
            LastRequeuedTaskId = taskId;
            LastRequeuedReason = reason;
            return Task.FromResult(true);
        }

        public Task<bool> RepromptTask(int taskId, string instructions, string? reason, CancellationToken cancellationToken = default)
        {
            LastRepromptedTaskId = taskId;
            LastRepromptedInstructions = instructions;
            LastRepromptedReason = reason;
            return Task.FromResult(true);
        }

        public Task<IReadOnlyList<TaskReviewHistoryDto>> GetTaskReviewHistory(int taskId, CancellationToken cancellationToken = default)
        {
            LastReviewTaskId = taskId;
            return Task.FromResult(ReviewHistoryResult);
        }
        public Task ResetState(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
