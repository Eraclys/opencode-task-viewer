using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using TaskViewer.Application.Orchestration;
using TaskViewer.Infrastructure.Orchestration;

namespace TaskViewer.Server.Api;

public static class OrchestrationEndpoints
{
    public static IEndpointRouteBuilder MapOrchestrationEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/orch/config", (IOrchestrationUseCases useCases) => Results.Json(useCases.GetPublicConfig()));

        app.MapGet(
            "/api/orch/mappings",
            async (IOrchestrationUseCases useCases) =>
            {
                return Results.Json(await useCases.ListMappingsAsync());
            })
            .WithApiExceptionHandling(
                "Error listing orchestrator mappings",
                _ => ApiErrorResult.BadGateway("Failed to list orchestration mappings"));

        app.MapPost(
            "/api/orch/mappings",
            async (OrchestrationRequestParsers.UpsertMappingPayload? body, IOrchestrationUseCases useCases) =>
            {
                var mapping = await useCases.UpsertMappingAsync(OrchestrationRequestParsers.ParseUpsertMapping(body));

                return Results.Json(mapping);
            })
            .WithApiExceptionHandling(
                "Error saving orchestrator mapping",
                error => error.Message.Contains("Missing", StringComparison.OrdinalIgnoreCase) || error.Message.Contains("not found", StringComparison.OrdinalIgnoreCase)
                    ? ApiErrorResult.BadRequest(error.Message)
                    : ApiErrorResult.BadGateway(error.Message));

        app.MapDelete(
            "/api/orch/mappings/{mappingId}",
            async (int mappingId, IOrchestrationUseCases useCases) =>
            {
                var ok = await useCases.DeleteMappingAsync(mappingId);

                if (!ok)
                    return Results.Json(new ErrorResponseDto { Error = "Mapping not found" }, statusCode: 404);

                return Results.Json(new HealthResponseDto { Ok = true });
            })
            .WithApiExceptionHandling(
                "Error deleting orchestrator mapping",
                error => error.Message.Contains("Invalid", StringComparison.OrdinalIgnoreCase) || error.Message.Contains("not found", StringComparison.OrdinalIgnoreCase)
                    ? ApiErrorResult.BadRequest(error.Message)
                    : ApiErrorResult.BadGateway(error.Message));

        app.MapGet(
            "/api/orch/instructions",
            async (int? mappingId, string? issueType, IOrchestrationUseCases useCases) =>
            {
                var result = await useCases.GetInstructionProfileAsync(mappingId, issueType);

                return Results.Json(result);
            })
            .WithApiExceptionHandling(
                "Error loading instruction profile",
                _ => ApiErrorResult.BadGateway("Failed to load instruction profile"));

        app.MapPost(
            "/api/orch/instructions",
            async (OrchestrationRequestParsers.UpsertInstructionProfilePayload? body, IOrchestrationUseCases useCases) =>
            {
                return Results.Json(await useCases.UpsertInstructionProfileAsync(OrchestrationRequestParsers.ParseUpsertInstructionProfile(body)));
            })
            .WithApiExceptionHandling(
                "Error saving instruction profile",
                error => error.Message.Contains("Missing", StringComparison.OrdinalIgnoreCase) || error.Message.Contains("not found", StringComparison.OrdinalIgnoreCase)
                    ? ApiErrorResult.BadRequest(error.Message)
                    : ApiErrorResult.BadGateway(error.Message));

        app.MapGet(
            "/api/orch/issues",
            async (HttpContext ctx, int? mappingId, string? issueType, string? severity, string? issueStatus, int? page, int? pageSize, string? ruleKeys, string? rules, string? rule, IOrchestrationUseCases useCases) =>
            {
                SetNoStore(ctx.Response);

                if (!mappingId.HasValue)
                    return Results.Json(
                        new ErrorResponseDto
                        {
                            Error = "Missing mappingId"
                        },
                        statusCode: 400);

                var selectedRuleKeys = !string.IsNullOrWhiteSpace(ruleKeys)
                    ? ruleKeys
                    : !string.IsNullOrWhiteSpace(rules)
                        ? rules
                        : rule;

                var result = await useCases.ListIssuesAsync(
                    mappingId.Value,
                    issueType,
                    severity,
                    issueStatus,
                    page,
                    pageSize,
                    selectedRuleKeys);

                return Results.Json(result);
            })
            .WithApiExceptionHandling(
                "Error loading SonarQube issues",
                error => error.Message.Contains("Mapping not found", StringComparison.OrdinalIgnoreCase)
                    ? ApiErrorResult.BadRequest(error.Message)
                    : ApiErrorResult.BadGateway(error.Message));

        app.MapGet(
            "/api/orch/rules",
            async (HttpContext ctx, int? mappingId, string? issueType, string? issueStatus, IOrchestrationUseCases useCases) =>
            {
                SetNoStore(ctx.Response);

                if (!mappingId.HasValue)
                    return Results.Json(
                        new ErrorResponseDto
                        {
                            Error = "Missing mappingId"
                        },
                        statusCode: 400);

                var result = await useCases.ListRulesAsync(mappingId.Value, issueType, issueStatus);

                return Results.Json(result);
            })
            .WithApiExceptionHandling(
                "Error loading SonarQube rules",
                error => error.Message.Contains("Mapping not found", StringComparison.OrdinalIgnoreCase)
                    ? ApiErrorResult.BadRequest(error.Message)
                    : ApiErrorResult.BadGateway(error.Message));

        app.MapPost(
            "/api/orch/enqueue",
            async (OrchestrationRequestParsers.EnqueueIssuesPayload? body, IOrchestrationUseCases useCases) =>
            {
                var result = await useCases.EnqueueIssuesAsync(OrchestrationRequestParsers.ParseEnqueueIssues(body));

                return Results.Json(result);
            })
            .WithApiExceptionHandling(
                "Error enqueueing SonarQube issues",
                error => error.Message.Contains("Missing", StringComparison.OrdinalIgnoreCase) || error.Message.Contains("No issues", StringComparison.OrdinalIgnoreCase) || error.Message.Contains("Mapping not found", StringComparison.OrdinalIgnoreCase)
                    ? ApiErrorResult.BadRequest(error.Message)
                    : ApiErrorResult.BadGateway(error.Message));

        app.MapPost(
            "/api/orch/enqueue-all",
            async (OrchestrationRequestParsers.EnqueueAllPayload? body, IOrchestrationUseCases useCases) =>
            {
                var result = await useCases.EnqueueAllMatchingAsync(OrchestrationRequestParsers.ParseEnqueueAll(body));

                return Results.Json(result);
            })
            .WithApiExceptionHandling(
                "Error enqueueing all matching SonarQube issues",
                error => error.Message.Contains("required", StringComparison.OrdinalIgnoreCase) || error.Message.Contains("Missing", StringComparison.OrdinalIgnoreCase) || error.Message.Contains("Mapping not found", StringComparison.OrdinalIgnoreCase)
                    ? ApiErrorResult.BadRequest(error.Message)
                    : ApiErrorResult.BadGateway(error.Message));

        app.MapGet(
            "/api/orch/queue",
            async (HttpContext ctx, string? states, int? limit, IOrchestrationUseCases useCases) =>
            {
                SetNoStore(ctx.Response);

                var result = await useCases.GetQueueAsync(states, limit);

                return Results.Json(result);
            })
            .WithApiExceptionHandling(
                "Error loading orchestration tasks",
                _ => ApiErrorResult.BadGateway("Failed to load orchestration tasks"));

        app.MapPost(
            "/api/orch/queue/{queueId}/cancel",
            async (int queueId, IOrchestrationUseCases useCases) =>
            {
                var ok = await useCases.CancelQueueItemAsync(queueId);

                if (!ok)
                    return Results.Json(
                        new ErrorResponseDto
                        {
                            Error = "Task not found or already terminal"
                        },
                        statusCode: 404);

                return Results.Json(
                    new HealthResponseDto
                    {
                        Ok = true
                    });
            })
            .WithApiExceptionHandling(
                "Error cancelling task",
                error => error.Message.Contains("Invalid queue id", StringComparison.OrdinalIgnoreCase)
                    ? ApiErrorResult.BadRequest(error.Message)
                    : ApiErrorResult.BadGateway(error.Message));

        app.MapPost(
            "/api/orch/queue/retry-failed",
            async (IOrchestrationUseCases useCases) =>
            {
                var retried = await useCases.RetryFailedAsync();

                return Results.Json(
                    new RetryFailedResponseDto
                    {
                        Retried = retried
                    });
            })
            .WithApiExceptionHandling(
                "Error retrying failed tasks",
                _ => ApiErrorResult.BadGateway("Failed to retry failed tasks"));

        app.MapPost(
            "/api/orch/queue/clear",
            async (IOrchestrationUseCases useCases) =>
            {
                var cleared = await useCases.ClearQueuedAsync();

                return Results.Json(
                    new ClearQueuedResponseDto
                    {
                        Cleared = cleared
                    });
            })
            .WithApiExceptionHandling(
                "Error clearing pending tasks",
                _ => ApiErrorResult.BadGateway("Failed to clear pending tasks"));

        app.MapPost(
            "/api/orch/tasks/{taskId}/approve",
            async (int taskId, IOrchestrationUseCases useCases) =>
            {
                var ok = await useCases.ApproveTaskAsync(taskId);

                if (!ok)
                    return Results.Json(new ErrorResponseDto { Error = "Task not found or not awaiting review" }, statusCode: 404);

                return Results.Json(new HealthResponseDto { Ok = true });
            })
            .WithApiExceptionHandling(
                "Error approving task",
                error => error.Message.Contains("Invalid task id", StringComparison.OrdinalIgnoreCase)
                    ? ApiErrorResult.BadRequest(error.Message)
                    : ApiErrorResult.BadGateway(error.Message));

        app.MapPost(
            "/api/orch/tasks/{taskId}/reject",
            async (int taskId, OrchestrationRequestParsers.TaskReviewPayload? body, IOrchestrationUseCases useCases) =>
            {
                var review = OrchestrationRequestParsers.ParseTaskReviewRequest(body);
                var ok = await useCases.RejectTaskAsync(taskId, review.Reason);

                if (!ok)
                    return Results.Json(new ErrorResponseDto { Error = "Task not found or not awaiting review" }, statusCode: 404);

                return Results.Json(new HealthResponseDto { Ok = true });
            })
            .WithApiExceptionHandling(
                "Error rejecting task",
                error => error.Message.Contains("Invalid task id", StringComparison.OrdinalIgnoreCase)
                    ? ApiErrorResult.BadRequest(error.Message)
                    : ApiErrorResult.BadGateway(error.Message));

        app.MapPost(
            "/api/orch/tasks/{taskId}/requeue",
            async (int taskId, OrchestrationRequestParsers.TaskReviewPayload? body, IOrchestrationUseCases useCases) =>
            {
                var review = OrchestrationRequestParsers.ParseTaskReviewRequest(body);
                var ok = await useCases.RequeueTaskAsync(taskId, review.Reason);

                if (!ok)
                    return Results.Json(new ErrorResponseDto { Error = "Task not found or not requeueable" }, statusCode: 404);

                return Results.Json(new HealthResponseDto { Ok = true });
            })
            .WithApiExceptionHandling(
                "Error requeueing task",
                error => error.Message.Contains("Invalid task id", StringComparison.OrdinalIgnoreCase)
                    ? ApiErrorResult.BadRequest(error.Message)
                    : ApiErrorResult.BadGateway(error.Message));

        app.MapPost(
            "/api/orch/tasks/{taskId}/reprompt",
            async (int taskId, OrchestrationRequestParsers.TaskReviewPayload? body, IOrchestrationUseCases useCases) =>
            {
                var review = OrchestrationRequestParsers.ParseTaskReviewRequest(body);
                var ok = await useCases.RepromptTaskAsync(taskId, review.Instructions ?? string.Empty, review.Reason);

                if (!ok)
                    return Results.Json(new ErrorResponseDto { Error = "Task not found or not repromptable" }, statusCode: 404);

                return Results.Json(new HealthResponseDto { Ok = true });
            })
            .WithApiExceptionHandling(
                "Error reprompting task",
                error => error.Message.Contains("Invalid task id", StringComparison.OrdinalIgnoreCase) || error.Message.Contains("Missing instructions", StringComparison.OrdinalIgnoreCase)
                    ? ApiErrorResult.BadRequest(error.Message)
                    : ApiErrorResult.BadGateway(error.Message));

        app.MapGet(
            "/api/orch/tasks/{taskId}/review-history",
            async (int taskId, HttpContext ctx, IOrchestrationUseCases useCases) =>
            {
                SetNoStore(ctx.Response);

                var items = await useCases.GetTaskReviewHistoryAsync(taskId);

                return Results.Json(
                    new TaskReviewHistoryListDto
                    {
                        Items = items
                    });
            })
            .WithApiExceptionHandling(
                "Error loading task review history",
                error => error.Message.Contains("Invalid task id", StringComparison.OrdinalIgnoreCase)
                    ? ApiErrorResult.BadRequest(error.Message)
                    : ApiErrorResult.BadGateway(error.Message));

        app.MapPost(
            "/api/test/orch/reset",
            async (IOrchestrationUseCases useCases) =>
            {
                await useCases.ResetStateAsync();

                return Results.Json(
                    new OrchestrationResetStateDto
                    {
                        Ok = true
                    });
            })
            .WithApiExceptionHandling(
                "Error resetting orchestration state",
                _ => ApiErrorResult.BadGateway("Failed to reset orchestration state"));

        return app;
    }

    static void SetNoStore(HttpResponse response)
    {
        response.Headers.CacheControl = "no-store, no-cache, must-revalidate, private";
        response.Headers.Pragma = "no-cache";
        response.Headers.Expires = "0";
    }

}
