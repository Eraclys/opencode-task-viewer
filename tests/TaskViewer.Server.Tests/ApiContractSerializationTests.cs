using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TaskViewer.Server.Api;
using TaskViewer.Server.Application.Orchestration;
using TaskViewer.Server.Application.Sessions;
using TaskViewer.Server.Infrastructure.Orchestration;

namespace TaskViewer.Server.Tests;

public sealed class ApiContractSerializationTests
{
    [Fact]
    public async Task ViewerHealth_UsesStableOkFieldName()
    {
        using var host = await CreateHost(endpoints => endpoints.MapViewerEndpoints());
        using var client = host.GetTestClient();

        using var response = await client.GetAsync("/api/health");
        var body = await response.Content.ReadAsStringAsync();

        response.EnsureSuccessStatusCode();

        using var json = JsonDocument.Parse(body);
        Assert.True(json.RootElement.TryGetProperty("ok", out var ok));
        Assert.True(ok.GetBoolean());
    }

    [Fact]
    public async Task SessionNotFound_UsesStableErrorFieldName()
    {
        var useCases = new FakeSessionsUseCases();

        using var host = await CreateHost(
            endpoints => endpoints.MapSessionsEndpoints(useCases));

        using var client = host.GetTestClient();
        using var response = await client.GetAsync("/api/sessions/missing-session");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(StatusCodes.Status404NotFound, (int)response.StatusCode);

        using var json = JsonDocument.Parse(body);
        Assert.True(json.RootElement.TryGetProperty("error", out var error));
        Assert.Equal("Session not found", error.GetString());
    }

    [Fact]
    public async Task LastAssistantMessage_UsesStableFieldNames()
    {
        var useCases = new FakeSessionsUseCases(
            lastAssistantMessageResult: new LastAssistantMessageResult(
                true,
                "sess-1",
                "done",
                DateTimeOffset.Parse("2026-01-01T00:00:00+00:00")));

        using var host = await CreateHost(
            endpoints => endpoints.MapSessionsEndpoints(useCases));

        using var client = host.GetTestClient();
        using var response = await client.GetAsync("/api/sessions/sess-1/last-assistant-message");
        var body = await response.Content.ReadAsStringAsync();

        response.EnsureSuccessStatusCode();

        using var json = JsonDocument.Parse(body);
        Assert.Equal("sess-1", json.RootElement.GetProperty("sessionId").GetString());
        Assert.Equal("done", json.RootElement.GetProperty("message").GetString());
        Assert.True(json.RootElement.TryGetProperty("createdAt", out _));
    }

    [Fact]
    public async Task TaskLastAssistantMessage_UsesStableFieldNames()
    {
        var useCases = new FakeSessionsUseCases(
            taskLastAssistantMessageResult: new LastAssistantMessageResult(
                true,
                "sess-1",
                "done",
                DateTimeOffset.Parse("2026-01-01T00:00:00+00:00")));

        using var host = await CreateHost(
            endpoints => endpoints.MapSessionsEndpoints(useCases));

        using var client = host.GetTestClient();
        using var response = await client.GetAsync("/api/tasks/board/queue-12/last-assistant-message");
        var body = await response.Content.ReadAsStringAsync();

        response.EnsureSuccessStatusCode();

        using var json = JsonDocument.Parse(body);
        Assert.Equal("sess-1", json.RootElement.GetProperty("sessionId").GetString());
        Assert.Equal("done", json.RootElement.GetProperty("message").GetString());
        Assert.True(json.RootElement.TryGetProperty("createdAt", out _));
    }

    [Fact]
    public async Task QueueRetry_UsesStableRetriedFieldName()
    {
        var useCases = new FakeOrchestrationUseCases(retried: 3);

        using var host = await CreateHost(
            endpoints => endpoints.MapOrchestrationEndpoints(useCases));

        using var client = host.GetTestClient();
        using var response = await client.PostAsync("/api/orch/queue/retry-failed", null);
        var body = await response.Content.ReadAsStringAsync();

        response.EnsureSuccessStatusCode();

        using var json = JsonDocument.Parse(body);
        Assert.Equal(3, json.RootElement.GetProperty("retried").GetInt32());
    }

    [Fact]
    public async Task QueueCancelNotFound_UsesStableErrorFieldName()
    {
        var useCases = new FakeOrchestrationUseCases(cancelQueueItemResult: false);

        using var host = await CreateHost(
            endpoints => endpoints.MapOrchestrationEndpoints(useCases));

        using var client = host.GetTestClient();
        using var response = await client.PostAsync("/api/orch/queue/123/cancel", null);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(StatusCodes.Status404NotFound, (int)response.StatusCode);

        using var json = JsonDocument.Parse(body);
        Assert.Equal("Task not found or already terminal", json.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task OrchestrationConfig_UsesStableFieldNames()
    {
        using var host = await CreateHost(
            endpoints => endpoints.MapOrchestrationEndpoints(new FakeOrchestrationUseCases()));

        using var client = host.GetTestClient();
        using var response = await client.GetAsync("/api/orch/config");
        var body = await response.Content.ReadAsStringAsync();

        response.EnsureSuccessStatusCode();

        using var json = JsonDocument.Parse(body);
        Assert.True(json.RootElement.GetProperty("configured").GetBoolean());
        Assert.Equal(3, json.RootElement.GetProperty("maxActive").GetInt32());
        Assert.Equal(3000, json.RootElement.GetProperty("pollMs").GetInt32());
        Assert.Equal(3, json.RootElement.GetProperty("maxAttempts").GetInt32());
        Assert.Equal(5, json.RootElement.GetProperty("maxWorkingGlobal").GetInt32());
        Assert.Equal(4, json.RootElement.GetProperty("workingResumeBelow").GetInt32());
    }

    [Fact]
    public async Task RulesMissingMappingId_UsesStableErrorFieldName()
    {
        using var host = await CreateHost(
            endpoints => endpoints.MapOrchestrationEndpoints(new FakeOrchestrationUseCases()));

        using var client = host.GetTestClient();
        using var response = await client.GetAsync("/api/orch/rules");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(StatusCodes.Status400BadRequest, (int)response.StatusCode);

        using var json = JsonDocument.Parse(body);
        Assert.Equal("Missing mappingId", json.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task QueueClear_UsesStableClearedFieldName()
    {
        using var host = await CreateHost(
            endpoints => endpoints.MapOrchestrationEndpoints(new FakeOrchestrationUseCases(cleared: 7)));

        using var client = host.GetTestClient();
        using var response = await client.PostAsync("/api/orch/queue/clear", null);
        var body = await response.Content.ReadAsStringAsync();

        response.EnsureSuccessStatusCode();

        using var json = JsonDocument.Parse(body);
        Assert.Equal(7, json.RootElement.GetProperty("cleared").GetInt32());
    }

    [Fact]
    public async Task TaskApproveEndpoint_UsesStableOkFieldName()
    {
        using var host = await CreateHost(
            endpoints => endpoints.MapOrchestrationEndpoints(new FakeOrchestrationUseCases()));

        using var client = host.GetTestClient();
        using var response = await client.PostAsync("/api/orch/tasks/12/approve", null);
        var body = await response.Content.ReadAsStringAsync();

        response.EnsureSuccessStatusCode();

        using var json = JsonDocument.Parse(body);
        Assert.True(json.RootElement.GetProperty("ok").GetBoolean());
    }

    [Fact]
    public async Task TaskRejectEndpoint_UsesStableOkFieldName()
    {
        using var host = await CreateHost(
            endpoints => endpoints.MapOrchestrationEndpoints(new FakeOrchestrationUseCases()));

        using var client = host.GetTestClient();
        using var response = await client.PostAsync("/api/orch/tasks/12/reject", JsonContent("{\"reason\":\"bad patch\"}"));
        var body = await response.Content.ReadAsStringAsync();

        response.EnsureSuccessStatusCode();

        using var json = JsonDocument.Parse(body);
        Assert.True(json.RootElement.GetProperty("ok").GetBoolean());
    }

    [Fact]
    public async Task TaskReviewEndpoints_AcceptReasonOnlyPayloads()
    {
        var useCases = new FakeOrchestrationUseCases();

        using var host = await CreateHost(
            endpoints => endpoints.MapOrchestrationEndpoints(useCases));

        using var client = host.GetTestClient();

        using var approveResponse = await client.PostAsync("/api/orch/tasks/12/approve", JsonContent("{}"));
        approveResponse.EnsureSuccessStatusCode();

        using var rejectResponse = await client.PostAsync("/api/orch/tasks/12/reject", JsonContent("{\"reason\":\"bad patch\"}"));
        rejectResponse.EnsureSuccessStatusCode();

        using var requeueResponse = await client.PostAsync("/api/orch/tasks/12/requeue", JsonContent("{\"reason\":\"retry\"}"));
        requeueResponse.EnsureSuccessStatusCode();

        Assert.Equal("bad patch", useCases.LastRejectedReason);
        Assert.Equal("retry", useCases.LastRequeuedReason);
    }

    [Fact]
    public async Task TaskRepromptEndpoint_AcceptsInstructionsAndReason()
    {
        var useCases = new FakeOrchestrationUseCases();

        using var host = await CreateHost(
            endpoints => endpoints.MapOrchestrationEndpoints(useCases));

        using var client = host.GetTestClient();
        using var response = await client.PostAsync(
            "/api/orch/tasks/12/reprompt",
            JsonContent("{\"instructions\":\"Retry with a smaller patch\",\"reason\":\"First pass touched too much\"}"));
        var body = await response.Content.ReadAsStringAsync();

        response.EnsureSuccessStatusCode();

        using var json = JsonDocument.Parse(body);
        Assert.True(json.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("Retry with a smaller patch", useCases.LastRepromptedInstructions);
        Assert.Equal("First pass touched too much", useCases.LastRepromptedReason);
    }

    [Fact]
    public async Task DeleteMappingEndpoint_UsesStableOkFieldName()
    {
        var useCases = new FakeOrchestrationUseCases();

        using var host = await CreateHost(
            endpoints => endpoints.MapOrchestrationEndpoints(useCases));

        using var client = host.GetTestClient();
        using var response = await client.DeleteAsync("/api/orch/mappings/12");
        var body = await response.Content.ReadAsStringAsync();

        response.EnsureSuccessStatusCode();

        using var json = JsonDocument.Parse(body);
        Assert.True(json.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("12", useCases.LastDeletedMappingId);
    }

    [Fact]
    public async Task TaskRequeueEndpoint_UsesStableOkFieldName()
    {
        using var host = await CreateHost(
            endpoints => endpoints.MapOrchestrationEndpoints(new FakeOrchestrationUseCases()));

        using var client = host.GetTestClient();
        using var response = await client.PostAsync("/api/orch/tasks/12/requeue", JsonContent("{\"reason\":\"retry with edited prompt\"}"));
        var body = await response.Content.ReadAsStringAsync();

        response.EnsureSuccessStatusCode();

        using var json = JsonDocument.Parse(body);
        Assert.True(json.RootElement.GetProperty("ok").GetBoolean());
    }

    [Fact]
    public async Task TaskReviewHistoryEndpoint_UsesStableItemsFieldName()
    {
        using var host = await CreateHost(
            endpoints => endpoints.MapOrchestrationEndpoints(new FakeOrchestrationUseCases()));

        using var client = host.GetTestClient();
        using var response = await client.GetAsync("/api/orch/tasks/12/review-history");
        var body = await response.Content.ReadAsStringAsync();

        response.EnsureSuccessStatusCode();

        using var json = JsonDocument.Parse(body);
        Assert.True(json.RootElement.TryGetProperty("items", out var items));
        Assert.Equal(JsonValueKind.Array, items.ValueKind);
    }

    [Fact]
    public async Task RulesSuccess_UsesStableFieldNames()
    {
        var useCases = new FakeOrchestrationUseCases(
            rulesResult: new RulesListDto
            {
                Mapping = CreateMappingRecord(),
                IssueType = "CODE_SMELL",
                IssueStatus = "OPEN",
                ScannedIssues = 12,
                Truncated = false,
                Rules =
                [
                    new RuleCountDto
                    {
                        Key = "javascript:S3776",
                        Name = "Cognitive Complexity",
                        Count = 4
                    }
                ]
            });

        using var host = await CreateHost(
            endpoints => endpoints.MapOrchestrationEndpoints(useCases));

        using var client = host.GetTestClient();
        using var response = await client.GetAsync("/api/orch/rules?mappingId=1&issueType=CODE_SMELL&issueStatus=OPEN");
        var body = await response.Content.ReadAsStringAsync();

        response.EnsureSuccessStatusCode();

        using var json = JsonDocument.Parse(body);
        Assert.Equal(1, json.RootElement.GetProperty("mapping").GetProperty("id").GetInt32());
        Assert.Equal("CODE_SMELL", json.RootElement.GetProperty("issueType").GetString());
        Assert.Equal("OPEN", json.RootElement.GetProperty("issueStatus").GetString());
        Assert.Equal(12, json.RootElement.GetProperty("scannedIssues").GetInt32());
        Assert.False(json.RootElement.GetProperty("truncated").GetBoolean());
        Assert.Equal("javascript:S3776", json.RootElement.GetProperty("rules")[0].GetProperty("key").GetString());
    }

    [Fact]
    public async Task IssuesSuccess_UsesStableFieldNames()
    {
        var useCases = new FakeOrchestrationUseCases(
            issuesResult: new IssuesListDto
            {
                Mapping = CreateMappingRecord(),
                Paging = new IssuesPagingDto
                {
                    PageIndex = 2,
                    PageSize = 25,
                    Total = 50
                },
                Issues =
                [
                    new IssueListItemDto
                    {
                        Key = "issue-1",
                        Type = "BUG",
                        Severity = "CRITICAL",
                        Rule = "csharpsquid:S1118",
                        Message = "Add a private constructor",
                        Component = "sample:src/File.cs",
                        Line = 18,
                        Status = "OPEN",
                        RelativePath = "src/File.cs",
                        AbsolutePath = "C:/Work/src/File.cs"
                    }
                ]
            });

        using var host = await CreateHost(
            endpoints => endpoints.MapOrchestrationEndpoints(useCases));

        using var client = host.GetTestClient();
        using var response = await client.GetAsync("/api/orch/issues?mappingId=1&page=2&pageSize=25");
        var body = await response.Content.ReadAsStringAsync();

        response.EnsureSuccessStatusCode();

        using var json = JsonDocument.Parse(body);
        Assert.Equal(1, json.RootElement.GetProperty("mapping").GetProperty("id").GetInt32());
        Assert.Equal(2, json.RootElement.GetProperty("paging").GetProperty("pageIndex").GetInt32());
        Assert.Equal(25, json.RootElement.GetProperty("paging").GetProperty("pageSize").GetInt32());
        Assert.Equal(50, json.RootElement.GetProperty("paging").GetProperty("total").GetInt32());
        Assert.Equal("issue-1", json.RootElement.GetProperty("issues")[0].GetProperty("key").GetString());
        Assert.Equal("src/File.cs", json.RootElement.GetProperty("issues")[0].GetProperty("relativePath").GetString());
    }

    [Fact]
    public async Task EnqueueIssuesSuccess_UsesStableFieldNames()
    {
        var useCases = new FakeOrchestrationUseCases(
            enqueueIssuesResult: new EnqueueIssuesResultDto
            {
                Created = 1,
                Skipped =
                [
                    new QueueEnqueueSkipView
                    {
                        IssueKey = "skip-1",
                        Reason = "already-queued"
                    }
                ],
                Items =
                [
                    CreateQueueItemRecord()
                ]
            });

        using var host = await CreateHost(
            endpoints => endpoints.MapOrchestrationEndpoints(useCases));

        using var client = host.GetTestClient();
        using var response = await client.PostAsync(
            "/api/orch/enqueue",
            JsonContent("""
            {
              "mappingId": 1,
              "issues": [ { "key": "issue-1" } ]
            }
            """));
        var body = await response.Content.ReadAsStringAsync();

        response.EnsureSuccessStatusCode();

        using var json = JsonDocument.Parse(body);
        Assert.Equal(1, json.RootElement.GetProperty("created").GetInt32());
        Assert.Equal("skip-1", json.RootElement.GetProperty("skipped")[0].GetProperty("issueKey").GetString());
        Assert.Equal("already-queued", json.RootElement.GetProperty("skipped")[0].GetProperty("reason").GetString());
        Assert.Equal(99, json.RootElement.GetProperty("items")[0].GetProperty("id").GetInt32());
        Assert.Equal("queued", json.RootElement.GetProperty("items")[0].GetProperty("state").GetString());
    }

    [Fact]
    public async Task EnqueueAllSuccess_UsesStableFieldNames()
    {
        var useCases = new FakeOrchestrationUseCases(
            enqueueAllResult: new EnqueueAllResultDto
            {
                Matched = 4,
                Created = 2,
                Truncated = true,
                Skipped =
                [
                    new QueueEnqueueSkipView
                    {
                        IssueKey = "skip-2",
                        Reason = "invalid-issue"
                    }
                ],
                Items =
                [
                    CreateQueueItemRecord()
                ]
            });

        using var host = await CreateHost(
            endpoints => endpoints.MapOrchestrationEndpoints(useCases));

        using var client = host.GetTestClient();
        using var response = await client.PostAsync(
            "/api/orch/enqueue-all",
            JsonContent("""
            {
              "mappingId": 1,
              "issueType": "CODE_SMELL"
            }
            """));
        var body = await response.Content.ReadAsStringAsync();

        response.EnsureSuccessStatusCode();

        using var json = JsonDocument.Parse(body);
        Assert.Equal(4, json.RootElement.GetProperty("matched").GetInt32());
        Assert.Equal(2, json.RootElement.GetProperty("created").GetInt32());
        Assert.True(json.RootElement.GetProperty("truncated").GetBoolean());
        Assert.Equal("invalid-issue", json.RootElement.GetProperty("skipped")[0].GetProperty("reason").GetString());
        Assert.Equal(99, json.RootElement.GetProperty("items")[0].GetProperty("id").GetInt32());
    }

    [Fact]
    public async Task QueueOverview_UsesStableFieldNames()
    {
        var useCases = new FakeOrchestrationUseCases(
            queueResult: new QueueOverviewDto
            {
                Items = [CreateQueueItemRecord()],
                Stats = new QueueStatsDto
                {
                    Queued = 2,
                    Dispatching = 1,
                    SessionCreated = 3,
                    Done = 4,
                    Failed = 5,
                    Cancelled = 6
                },
                Worker = new OrchestrationWorkerStateDto
                {
                    InFlightDispatches = 1,
                    MaxActiveDispatches = 3,
                    PausedByWorking = true,
                    WorkingCount = 2,
                    MaxWorkingGlobal = 5,
                    WorkingResumeBelow = 4,
                    WorkingSampleAt = DateTimeOffset.Parse("2026-01-03T00:00:00+00:00")
                }
            });

        using var host = await CreateHost(
            endpoints => endpoints.MapOrchestrationEndpoints(useCases));

        using var client = host.GetTestClient();
        using var response = await client.GetAsync("/api/orch/queue?states=queued,dispatching&limit=25");
        var body = await response.Content.ReadAsStringAsync();

        response.EnsureSuccessStatusCode();

        using var json = JsonDocument.Parse(body);
        Assert.Equal(99, json.RootElement.GetProperty("items")[0].GetProperty("id").GetInt32());
        Assert.Equal(2, json.RootElement.GetProperty("stats").GetProperty("queued").GetInt32());
        Assert.Equal(3, json.RootElement.GetProperty("stats").GetProperty("session_created").GetInt32());
        Assert.True(json.RootElement.GetProperty("worker").GetProperty("pausedByWorking").GetBoolean());
        Assert.Equal(3, json.RootElement.GetProperty("worker").GetProperty("maxActiveDispatches").GetInt32());
    }

    [Fact]
    public async Task IssuesMissingMappingId_UsesStableErrorFieldName()
    {
        using var host = await CreateHost(
            endpoints => endpoints.MapOrchestrationEndpoints(new FakeOrchestrationUseCases()));

        using var client = host.GetTestClient();
        using var response = await client.GetAsync("/api/orch/issues");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(StatusCodes.Status400BadRequest, (int)response.StatusCode);

        using var json = JsonDocument.Parse(body);
        Assert.Equal("Missing mappingId", json.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task IssuesMappingNotFound_UsesStableErrorFieldNameAndStatus400()
    {
        using var host = await CreateHost(
            endpoints => endpoints.MapOrchestrationEndpoints(
                new FakeOrchestrationUseCases(listIssuesException: new InvalidOperationException("Mapping not found"))));

        using var client = host.GetTestClient();
        using var response = await client.GetAsync("/api/orch/issues?mappingId=1");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(StatusCodes.Status400BadRequest, (int)response.StatusCode);

        using var json = JsonDocument.Parse(body);
        Assert.Equal("Mapping not found", json.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task EnqueueIssuesValidationFailure_UsesStableErrorFieldNameAndStatus400()
    {
        using var host = await CreateHost(
            endpoints => endpoints.MapOrchestrationEndpoints(
                new FakeOrchestrationUseCases(enqueueIssuesException: new InvalidOperationException("No issues provided"))));

        using var client = host.GetTestClient();
        using var response = await client.PostAsync(
            "/api/orch/enqueue",
            JsonContent("""
            {
              "mappingId": 1,
              "issues": []
            }
            """));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(StatusCodes.Status400BadRequest, (int)response.StatusCode);

        using var json = JsonDocument.Parse(body);
        Assert.Equal("No issues provided", json.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task EnqueueAllValidationFailure_UsesStableErrorFieldNameAndStatus400()
    {
        using var host = await CreateHost(
            endpoints => endpoints.MapOrchestrationEndpoints(
                new FakeOrchestrationUseCases(enqueueAllException: new InvalidOperationException("mappingId is required"))));

        using var client = host.GetTestClient();
        using var response = await client.PostAsync(
            "/api/orch/enqueue-all",
            JsonContent("""
            {
              "issueType": "CODE_SMELL"
            }
            """));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(StatusCodes.Status400BadRequest, (int)response.StatusCode);

        using var json = JsonDocument.Parse(body);
        Assert.Equal("mappingId is required", json.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task QueueLoadFailure_UsesStableErrorFieldName()
    {
        using var host = await CreateHost(
            endpoints => endpoints.MapOrchestrationEndpoints(
                new FakeOrchestrationUseCases(queueException: new InvalidOperationException("queue boom"))));

        using var client = host.GetTestClient();
        using var response = await client.GetAsync("/api/orch/queue");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(StatusCodes.Status502BadGateway, (int)response.StatusCode);

        using var json = JsonDocument.Parse(body);
        Assert.Equal("Failed to load orchestration tasks", json.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task ListMappingsFailure_UsesStableErrorFieldName()
    {
        using var host = await CreateHost(
            endpoints => endpoints.MapOrchestrationEndpoints(new FakeOrchestrationUseCases(listMappingsException: new InvalidOperationException("boom"))));

        using var client = host.GetTestClient();
        using var response = await client.GetAsync("/api/orch/mappings");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(StatusCodes.Status502BadGateway, (int)response.StatusCode);

        using var json = JsonDocument.Parse(body);
        Assert.Equal("Failed to list orchestration mappings", json.RootElement.GetProperty("error").GetString());
    }

    static async Task<IHost> CreateHost(Action<IEndpointRouteBuilder> mapEndpoints, Action<IServiceCollection>? configureServices = null)
    {
        var host = await new HostBuilder()
            .ConfigureWebHost(
                webBuilder => webBuilder
                    .UseTestServer()
                    .ConfigureServices(
                        services =>
                        {
                            services.AddRouting();
                            configureServices?.Invoke(services);
                        })
                    .Configure(
                        app =>
                        {
                            app.UseRouting();
                            app.UseEndpoints(mapEndpoints);
                        }))
            .StartAsync();

        return host;
    }

    static StringContent JsonContent(string json)
        => new(json, System.Text.Encoding.UTF8, "application/json");

    static MappingRecord CreateMappingRecord()
        => new()
        {
            Id = 1,
            SonarProjectKey = "sample-project",
            Directory = "C:/Work/Sample",
            Branch = "main",
            Enabled = true,
            CreatedAt = DateTimeOffset.Parse("2026-01-01T00:00:00+00:00"),
            UpdatedAt = DateTimeOffset.Parse("2026-01-02T00:00:00+00:00")
        };

    static QueueItemRecord CreateQueueItemRecord()
        => new()
        {
            Id = 99,
            IssueKey = "issue-1",
            MappingId = 1,
            SonarProjectKey = "sample-project",
            Directory = "C:/Work/Sample",
            Branch = "main",
            IssueType = "CODE_SMELL",
            Severity = "MAJOR",
            Rule = "javascript:S3776",
            Message = "Reduce complexity",
            Component = "sample-project:src/file.js",
            RelativePath = "src/file.js",
            AbsolutePath = "C:/Work/Sample/src/file.js",
            Line = 12,
            IssueStatus = "OPEN",
            Instructions = "keep it simple",
            State = "queued",
            AttemptCount = 0,
            MaxAttempts = 3,
            SessionId = null,
            OpenCodeUrl = null,
            LastError = null,
            CreatedAt = DateTimeOffset.Parse("2026-01-01T00:00:00+00:00"),
            UpdatedAt = DateTimeOffset.Parse("2026-01-01T00:00:00+00:00")
        };

    static QueueOverviewDto CreateQueueOverviewDto()
        => new()
        {
            Items = [],
            Stats = new QueueStatsDto
            {
                Queued = 0,
                Dispatching = 0,
                SessionCreated = 0,
                Done = 0,
                Failed = 0,
                Cancelled = 0
            },
            Worker = new OrchestrationWorkerStateDto
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

    sealed class FakeSessionsUseCases(
        SessionTasksResult? sessionTasksResult = null,
        LastAssistantMessageResult? lastAssistantMessageResult = null,
        LastAssistantMessageResult? taskLastAssistantMessageResult = null,
        ArchiveSessionResult? archiveSessionResult = null) : ISessionsUseCases
    {
        readonly SessionTasksResult _sessionTasksResult = sessionTasksResult ?? new SessionTasksResult(false, []);
        readonly LastAssistantMessageResult _lastAssistantMessageResult = lastAssistantMessageResult ?? new LastAssistantMessageResult(false, "missing-session", null, null);
        readonly LastAssistantMessageResult _taskLastAssistantMessageResult = taskLastAssistantMessageResult ?? new LastAssistantMessageResult(false, "missing-task", null, null);
        readonly ArchiveSessionResult _archiveSessionResult = archiveSessionResult ?? new ArchiveSessionResult(false, null);

        public Task<IReadOnlyList<SessionSummaryDto>> ListSessionsAsync(string? limitParam)
            => Task.FromResult<IReadOnlyList<SessionSummaryDto>>([]);

        public Task<SessionTasksResult> GetSessionTasksAsync(string sessionId)
            => Task.FromResult(_sessionTasksResult);

        public Task<LastAssistantMessageResult> GetTaskLastAssistantMessageAsync(string taskId)
            => Task.FromResult(_taskLastAssistantMessageResult);

        public Task<LastAssistantMessageResult> GetLastAssistantMessageAsync(string sessionId)
            => Task.FromResult(_lastAssistantMessageResult);

        public Task<ArchiveSessionResult> ArchiveSessionAsync(string sessionId)
            => Task.FromResult(_archiveSessionResult);
    }

    sealed class FakeOrchestrationUseCases(
        int retried = 0,
        int cleared = 0,
        bool cancelQueueItemResult = true,
        Exception? listMappingsException = null,
        Exception? listIssuesException = null,
        Exception? enqueueIssuesException = null,
        Exception? enqueueAllException = null,
        Exception? queueException = null,
        RulesListDto? rulesResult = null,
        IssuesListDto? issuesResult = null,
        EnqueueIssuesResultDto? enqueueIssuesResult = null,
        EnqueueAllResultDto? enqueueAllResult = null,
        QueueOverviewDto? queueResult = null) : IOrchestrationUseCases
    {
        public string? LastRejectedReason { get; private set; }
        public string? LastRequeuedReason { get; private set; }
        public string? LastRepromptedInstructions { get; private set; }
        public string? LastRepromptedReason { get; private set; }
        public string? LastDeletedMappingId { get; private set; }

        public OrchestrationConfigDto GetPublicConfig() => new()
        {
            Configured = true,
            MaxActive = 3,
            PollMs = 3000,
            MaxAttempts = 3,
            MaxWorkingGlobal = 5,
            WorkingResumeBelow = 4
        };

        public Task<List<MappingRecord>> ListMappingsAsync()
            => listMappingsException is null
                ? Task.FromResult(new List<MappingRecord>())
                : Task.FromException<List<MappingRecord>>(listMappingsException);
        public Task<bool> DeleteMappingAsync(string mappingId)
        {
            LastDeletedMappingId = mappingId;
            return Task.FromResult(true);
        }
        public Task<MappingRecord> UpsertMappingAsync(UpsertMappingRequest request) => throw new NotSupportedException();
        public Task<InstructionProfileDto> GetInstructionProfileAsync(string? mappingId, string? issueType) => throw new NotSupportedException();
        public Task<InstructionProfileDto> UpsertInstructionProfileAsync(UpsertInstructionProfileRequest request) => throw new NotSupportedException();
        public Task<IssuesListDto> ListIssuesAsync(string mappingId, string? issueType, string? severity, string? issueStatus, string? page, string? pageSize, string? ruleKeys)
            => listIssuesException is null
                ? Task.FromResult(
                    issuesResult ??
                    new IssuesListDto
                    {
                        Mapping = CreateMappingRecord(),
                        Paging = new IssuesPagingDto
                        {
                            PageIndex = 1,
                            PageSize = 100,
                            Total = 0
                        },
                        Issues = []
                    })
                : Task.FromException<IssuesListDto>(listIssuesException);

        public Task<RulesListDto> ListRulesAsync(string mappingId, string? issueType, string? issueStatus)
            => Task.FromResult(
                rulesResult ??
                new RulesListDto
                {
                    Mapping = CreateMappingRecord(),
                    IssueType = issueType,
                    IssueStatus = issueStatus,
                    ScannedIssues = 0,
                    Truncated = false,
                    Rules = []
                });

        public Task<EnqueueIssuesResultDto> EnqueueIssuesAsync(EnqueueIssuesRequest request)
            => enqueueIssuesException is null
                ? Task.FromResult(
                    enqueueIssuesResult ??
                    new EnqueueIssuesResultDto
                    {
                        Created = 0,
                        Skipped = [],
                        Items = []
                    })
                : Task.FromException<EnqueueIssuesResultDto>(enqueueIssuesException);

        public Task<EnqueueAllResultDto> EnqueueAllMatchingAsync(EnqueueAllRequest request)
            => enqueueAllException is null
                ? Task.FromResult(
                    enqueueAllResult ??
                    new EnqueueAllResultDto
                    {
                        Matched = 0,
                        Created = 0,
                        Truncated = false,
                        Skipped = [],
                        Items = []
                    })
                : Task.FromException<EnqueueAllResultDto>(enqueueAllException);

        public Task<QueueOverviewDto> GetQueueAsync(string? states, string? limit)
            => queueException is null
                ? Task.FromResult(queueResult ?? CreateQueueOverviewDto())
                : Task.FromException<QueueOverviewDto>(queueException);
        public Task<bool> CancelQueueItemAsync(string queueId) => Task.FromResult(cancelQueueItemResult);
        public Task<int> RetryFailedAsync() => Task.FromResult(retried);
        public Task<int> ClearQueuedAsync() => Task.FromResult(cleared);
        public Task<bool> ApproveTaskAsync(string taskId)
        {
            return Task.FromResult(true);
        }

        public Task<bool> RejectTaskAsync(string taskId, string? reason)
        {
            LastRejectedReason = reason;
            return Task.FromResult(true);
        }

        public Task<bool> RequeueTaskAsync(string taskId, string? reason)
        {
            LastRequeuedReason = reason;
            return Task.FromResult(true);
        }

        public Task<bool> RepromptTaskAsync(string taskId, string instructions, string? reason)
        {
            LastRepromptedInstructions = instructions;
            LastRepromptedReason = reason;
            return Task.FromResult(true);
        }
        public Task<IReadOnlyList<TaskReviewHistoryDto>> GetTaskReviewHistoryAsync(string taskId)
            => Task.FromResult<IReadOnlyList<TaskReviewHistoryDto>>(
            [
                new TaskReviewHistoryDto
                {
                    Action = "rejected",
                    Reason = "Needs prompt tuning",
                    CreatedAt = DateTimeOffset.Parse("2026-03-08T10:04:00Z")
                }
            ]);
        public Task ResetStateAsync() => Task.CompletedTask;
    }
}
