using Microsoft.Extensions.Logging.Abstractions;
using OpenCode.Client;
using SonarQube.OpenCodeTaskViewer.Domain.Orchestration;
using SonarQube.OpenCodeTaskViewer.Infrastructure.Persistence;
using SonarQube.OpenCodeTaskViewer.Persistence;
using SonarQube.OpenCodeTaskViewer.Server.BackgroundServices;
using SonarQube.OpenCodeTaskViewer.Server.Configuration;

namespace SonarQube.OpenCodeTaskViewer.Server.Tests;

public sealed class SonarOrchestratorHostedServiceTests
{
    [Fact]
    public async Task StartAsync_RunsImmediateTickAndPeriodicTicks()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"taskviewer-hosted-orchestrator-{Guid.NewGuid():N}.sqlite");
        await using var persistence = new SqliteOrchestrationPersistence(dbPath, () => { });

        await using var orchestrator = new SonarOrchestrator(
            new SonarOrchestratorOptions
            {
                SonarUrl = "http://sonar.local",
                SonarToken = "token",
                DbPath = dbPath,
                Persistence = persistence,
                MaxActive = 1,
                PerProjectMaxActive = 1,
                PollMs = 25,
                LeaseSeconds = 180,
                MaxAttempts = 1,
                MaxWorkingGlobal = 0,
                WorkingResumeBelow = 0,
                OpenCodeApiClient = new DisabledOpenCodeService(),
                TaskReadinessGate = new TestTaskReadinessGate(),
                TaskSchedulerService = new RecordingTaskSchedulerService(),
                QueueDispatchService = new NoOpQueueDispatchService(),
                NormalizeDirectory = value => value,
                BuildOpenCodeSessionUrl = (_, _) => null,
                OnChange = () => { }
            });

        var hostedService = new SonarOrchestratorHostedService(
            orchestrator,
            new AppRuntimeSettings(
                new ViewerRuntimeSettings("127.0.0.1", 8080),
                new OpenCodeRuntimeSettings("http://localhost:4096", "opencode", "secret"),
                new SonarQubeRuntimeSettings("http://sonar.local", "token", SonarQubeMode.Real),
                new OrchestrationRuntimeSettings(
                    dbPath,
                    1,
                    1,
                    25,
                    180,
                    1,
                    0,
                    0)),
            NullLogger<SonarOrchestratorHostedService>.Instance);

        await hostedService.StartAsync(CancellationToken.None);
        await Task.Delay(90);
        await hostedService.StopAsync(CancellationToken.None);

        Assert.True(RecordingTaskSchedulerService.LeaseCalls >= 2);
    }

    sealed class RecordingTaskSchedulerService : ITaskSchedulerService
    {
        public static int LeaseCalls;

        public RecordingTaskSchedulerService()
        {
            LeaseCalls = 0;
        }

        public Task<QueueItemRecord?> LeaseNextTaskAsync(
            string leaseOwner,
            int globalMaxActive,
            int perProjectMaxActive,
            int leaseSeconds)
        {
            Interlocked.Increment(ref LeaseCalls);

            return Task.FromResult<QueueItemRecord?>(null);
        }
    }

    sealed class NoOpQueueDispatchService : IQueueDispatchService
    {
        public Task<QueueDispatchResult> DispatchAsync(QueueItemRecord item, IReadOnlyList<NormalizedIssue> issues)
            => Task.FromResult(new QueueDispatchResult("session", null));
    }
}
