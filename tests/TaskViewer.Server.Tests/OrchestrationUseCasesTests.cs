using TaskViewer.Server.Application.Orchestration;
using TaskViewer.Server.Infrastructure.Orchestration;

namespace TaskViewer.Server.Tests;

public sealed class OrchestrationUseCasesTests
{
    [Fact]
    public async Task GetInstructionProfileAsync_UppercasesIssueTypeAndParsesMappingId()
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
        var result = await sut.GetInstructionProfileAsync("42", "code_smell");

        Assert.Equal(42, result.MappingId);
        Assert.Equal("CODE_SMELL", result.IssueType);
        Assert.Equal("Apply safe fix", result.Instructions);
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
        var result = await sut.GetQueueAsync("queued", "100");

        Assert.NotNull(result.Items);
        Assert.NotNull(result.Stats);
        Assert.NotNull(result.Worker);
    }

    sealed class FakeGateway : IOrchestrationGateway
    {
        public string? LastRuleKeys { get; private set; }

        public InstructionProfileRecord? InstructionProfileResult { get; set; }
        public List<QueueItemRecord> QueueItemsResult { get; set; } = [];

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

        public Task<List<MappingRecord>> ListMappings() => Task.FromResult(new List<MappingRecord>());

        public Task<MappingRecord> UpsertMapping(UpsertMappingRequest request) => Task.FromResult(
            new MappingRecord
            {
                Id = 1,
                SonarProjectKey = "key",
                Directory = "C:/Work",
                CreatedAt = DateTimeOffset.UnixEpoch,
                UpdatedAt = DateTimeOffset.UnixEpoch
            });

        public Task<InstructionProfileRecord?> GetInstructionProfile(int? mappingId, string? issueType) => Task.FromResult(InstructionProfileResult);

        public Task<InstructionProfileRecord> UpsertInstructionProfile(UpsertInstructionProfileRequest request)
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
            int? mappingId,
            string? issueType,
            string? severity,
            string? issueStatus,
            string? page,
            string? pageSize,
            string? ruleKeys)
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

        public Task<RulesListDto> ListRules(int? mappingId, string? issueType, string? issueStatus)
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

        public Task<EnqueueIssuesResultDto> EnqueueIssues(EnqueueIssuesRequest request)
            => Task.FromResult(
                new EnqueueIssuesResultDto
                {
                    Created = 0,
                    Skipped = [],
                    Items = []
                });

        public Task<EnqueueAllResultDto> EnqueueAllMatching(EnqueueAllRequest request)
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

        public Task<List<QueueItemRecord>> ListQueue(string? states, string? limit) => Task.FromResult(QueueItemsResult);
        public Task<QueueStatsDto> GetQueueStats() => Task.FromResult(QueueStatsResult);
        public Task<OrchestrationWorkerStateDto> GetWorkerState() => Task.FromResult(WorkerStateResult);
        public Task<bool> CancelQueueItem(int? queueId) => Task.FromResult(true);
        public Task<int> RetryFailed() => Task.FromResult(0);
        public Task<int> ClearQueued() => Task.FromResult(0);
    }
}
