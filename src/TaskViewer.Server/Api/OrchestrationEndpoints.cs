using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using System.Text;
using TaskViewer.Application.Orchestration;
using TaskViewer.Infrastructure.Orchestration;

namespace TaskViewer.Server.Api;

public static class OrchestrationEndpoints
{
    public static IEndpointRouteBuilder MapOrchestrationEndpoints(this IEndpointRouteBuilder app, IOrchestrationUseCases useCases)
    {
        app.MapGet("/api/orch/config", () => Results.Json(useCases.GetPublicConfig()));

        app.MapGet(
            "/api/orch/mappings",
            async () =>
            {
                try
                {
                    return Results.Json(await useCases.ListMappingsAsync());
                }
                catch (Exception error)
                {
                    Console.Error.WriteLine($"Error listing orchestrator mappings: {error}");

                    return Results.Json(
                        new ErrorResponseDto
                        {
                            Error = "Failed to list orchestration mappings"
                        },
                        statusCode: 502);
                }
            });

        app.MapPost(
            "/api/orch/mappings",
            async (HttpContext ctx) =>
            {
                try
                {
                    var body = await ReadOptionalJsonBodyAsync(ctx.Request);
                    var mapping = await useCases.UpsertMappingAsync(OrchestrationRequestParsers.ParseUpsertMapping(body));

                    return Results.Json(mapping);
                }
                catch (Exception error)
                {
                    var message = error.Message;
                    var status = message.Contains("Missing", StringComparison.OrdinalIgnoreCase) || message.Contains("not found", StringComparison.OrdinalIgnoreCase) ? 400 : 502;
                    Console.Error.WriteLine($"Error saving orchestrator mapping: {error}");

                    return Results.Json(
                        new ErrorResponseDto
                        {
                            Error = message
                        },
                        statusCode: status);
                }
            });

        app.MapDelete(
            "/api/orch/mappings/{mappingId}",
            async (string mappingId) =>
            {
                try
                {
                    var ok = await useCases.DeleteMappingAsync(mappingId);

                    if (!ok)
                        return Results.Json(new ErrorResponseDto { Error = "Mapping not found" }, statusCode: 404);

                    return Results.Json(new HealthResponseDto { Ok = true });
                }
                catch (Exception error)
                {
                    var message = error.Message;
                    var status = message.Contains("Invalid", StringComparison.OrdinalIgnoreCase) || message.Contains("not found", StringComparison.OrdinalIgnoreCase) ? 400 : 502;
                    Console.Error.WriteLine($"Error deleting orchestrator mapping: {error}");

                    return Results.Json(
                        new ErrorResponseDto
                        {
                            Error = message
                        },
                        statusCode: status);
                }
            });

        app.MapGet(
            "/api/orch/instructions",
            async (HttpContext ctx) =>
            {
                try
                {
                    var result = await useCases.GetInstructionProfileAsync(
                        ctx.Request.Query["mappingId"].ToString(),
                        ctx.Request.Query["issueType"].ToString());

                    return Results.Json(result);
                }
                catch (Exception error)
                {
                    Console.Error.WriteLine($"Error loading instruction profile: {error}");

                    return Results.Json(
                        new ErrorResponseDto
                        {
                            Error = "Failed to load instruction profile"
                        },
                        statusCode: 502);
                }
            });

        app.MapPost(
            "/api/orch/instructions",
            async (HttpContext ctx) =>
            {
                try
                {
                    var body = await ReadOptionalJsonBodyAsync(ctx.Request);

                    return Results.Json(await useCases.UpsertInstructionProfileAsync(OrchestrationRequestParsers.ParseUpsertInstructionProfile(body)));
                }
                catch (Exception error)
                {
                    var message = error.Message;
                    var status = message.Contains("Missing", StringComparison.OrdinalIgnoreCase) || message.Contains("not found", StringComparison.OrdinalIgnoreCase) ? 400 : 502;
                    Console.Error.WriteLine($"Error saving instruction profile: {error}");

                    return Results.Json(
                        new ErrorResponseDto
                        {
                            Error = message
                        },
                        statusCode: status);
                }
            });

        app.MapGet(
            "/api/orch/issues",
            async (HttpContext ctx) =>
            {
                SetNoStore(ctx.Response);

                try
                {
                    var mappingId = ctx.Request.Query["mappingId"].ToString();

                    if (string.IsNullOrWhiteSpace(mappingId))
                        return Results.Json(
                            new ErrorResponseDto
                            {
                                Error = "Missing mappingId"
                            },
                            statusCode: 400);

                    var ruleKeys = ctx.Request.Query["ruleKeys"].ToString() is { Length: > 0 } rk
                        ? rk
                        : ctx.Request.Query["rules"].ToString() is { Length: > 0 } rs
                            ? rs
                            : ctx.Request.Query["rule"].ToString();

                    var result = await useCases.ListIssuesAsync(
                        mappingId,
                        ctx.Request.Query["issueType"].ToString(),
                        ctx.Request.Query["severity"].ToString(),
                        ctx.Request.Query["issueStatus"].ToString(),
                        ctx.Request.Query["page"].ToString(),
                        ctx.Request.Query["pageSize"].ToString(),
                        ruleKeys);

                    return Results.Json(result);
                }
                catch (Exception error)
                {
                    var message = error.Message;
                    var status = message.Contains("Mapping not found", StringComparison.OrdinalIgnoreCase) ? 400 : 502;
                    Console.Error.WriteLine($"Error loading SonarQube issues: {error}");

                    return Results.Json(
                        new ErrorResponseDto
                        {
                            Error = message
                        },
                        statusCode: status);
                }
            });

        app.MapGet(
            "/api/orch/rules",
            async (HttpContext ctx) =>
            {
                SetNoStore(ctx.Response);

                try
                {
                    var mappingId = ctx.Request.Query["mappingId"].ToString();

                    if (string.IsNullOrWhiteSpace(mappingId))
                        return Results.Json(
                            new ErrorResponseDto
                            {
                                Error = "Missing mappingId"
                            },
                            statusCode: 400);

                    var result = await useCases.ListRulesAsync(
                        mappingId,
                        ctx.Request.Query["issueType"].ToString(),
                        ctx.Request.Query["issueStatus"].ToString());

                    return Results.Json(result);
                }
                catch (Exception error)
                {
                    var message = error.Message;
                    var status = message.Contains("Mapping not found", StringComparison.OrdinalIgnoreCase) ? 400 : 502;
                    Console.Error.WriteLine($"Error loading SonarQube rules: {error}");

                    return Results.Json(
                        new ErrorResponseDto
                        {
                            Error = message
                        },
                        statusCode: status);
                }
            });

        app.MapPost(
            "/api/orch/enqueue",
            async (HttpContext ctx) =>
            {
                try
                {
                    var body = await ReadOptionalJsonBodyAsync(ctx.Request);
                    var result = await useCases.EnqueueIssuesAsync(OrchestrationRequestParsers.ParseEnqueueIssues(body));

                    return Results.Json(result);
                }
                catch (Exception error)
                {
                    var message = error.Message;

                    var status = message.Contains("Missing", StringComparison.OrdinalIgnoreCase) || message.Contains("No issues", StringComparison.OrdinalIgnoreCase) || message.Contains("Mapping not found", StringComparison.OrdinalIgnoreCase)
                        ? 400
                        : 502;

                    Console.Error.WriteLine($"Error enqueueing SonarQube issues: {error}");

                    return Results.Json(
                        new ErrorResponseDto
                        {
                            Error = message
                        },
                        statusCode: status);
                }
            });

        app.MapPost(
            "/api/orch/enqueue-all",
            async (HttpContext ctx) =>
            {
                try
                {
                    var body = await ReadOptionalJsonBodyAsync(ctx.Request);
                    var result = await useCases.EnqueueAllMatchingAsync(OrchestrationRequestParsers.ParseEnqueueAll(body));

                    return Results.Json(result);
                }
                catch (Exception error)
                {
                    var message = error.Message;

                    var status = message.Contains("required", StringComparison.OrdinalIgnoreCase) || message.Contains("Missing", StringComparison.OrdinalIgnoreCase) || message.Contains("Mapping not found", StringComparison.OrdinalIgnoreCase)
                        ? 400
                        : 502;

                    Console.Error.WriteLine($"Error enqueueing all matching SonarQube issues: {error}");

                    return Results.Json(
                        new ErrorResponseDto
                        {
                            Error = message
                        },
                        statusCode: status);
                }
            });

        app.MapGet(
            "/api/orch/queue",
            async (HttpContext ctx) =>
            {
                SetNoStore(ctx.Response);

                try
                {
                    var result = await useCases.GetQueueAsync(
                        ctx.Request.Query["states"].ToString(),
                        ctx.Request.Query["limit"].ToString());

                    return Results.Json(result);
                }
                catch (Exception error)
                {
                    Console.Error.WriteLine($"Error loading orchestration tasks: {error}");

                    return Results.Json(
                        new ErrorResponseDto
                        {
                            Error = "Failed to load orchestration tasks"
                        },
                        statusCode: 502);
                }
            });

        app.MapPost(
            "/api/orch/queue/{queueId}/cancel",
            async (string queueId) =>
            {
                try
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
                }
                catch (Exception error)
                {
                    var message = error.Message;
                    var status = message.Contains("Invalid queue id", StringComparison.OrdinalIgnoreCase) ? 400 : 502;
                    Console.Error.WriteLine($"Error cancelling task: {error}");

                    return Results.Json(
                        new ErrorResponseDto
                        {
                            Error = message
                        },
                        statusCode: status);
                }
            });

        app.MapPost(
            "/api/orch/queue/retry-failed",
            async () =>
            {
                try
                {
                    var retried = await useCases.RetryFailedAsync();

                    return Results.Json(
                        new RetryFailedResponseDto
                        {
                            Retried = retried
                        });
                }
                catch (Exception error)
                {
                    Console.Error.WriteLine($"Error retrying failed tasks: {error}");

                    return Results.Json(
                        new ErrorResponseDto
                        {
                            Error = "Failed to retry failed tasks"
                        },
                        statusCode: 502);
                }
            });

        app.MapPost(
            "/api/orch/queue/clear",
            async () =>
            {
                try
                {
                    var cleared = await useCases.ClearQueuedAsync();

                    return Results.Json(
                        new ClearQueuedResponseDto
                        {
                            Cleared = cleared
                        });
                }
                catch (Exception error)
                {
                    Console.Error.WriteLine($"Error clearing pending tasks: {error}");

                    return Results.Json(
                        new ErrorResponseDto
                        {
                            Error = "Failed to clear pending tasks"
                        },
                        statusCode: 502);
                }
            });

        app.MapPost(
            "/api/orch/tasks/{taskId}/approve",
            async (string taskId, HttpContext ctx) =>
            {
                try
                {
                    var body = await ReadOptionalJsonBodyAsync(ctx.Request);
                    var ok = await useCases.ApproveTaskAsync(taskId);

                    if (!ok)
                        return Results.Json(new ErrorResponseDto { Error = "Task not found or not awaiting review" }, statusCode: 404);

                    return Results.Json(new HealthResponseDto { Ok = true });
                }
                catch (Exception error)
                {
                    var message = error.Message;
                    var status = message.Contains("Invalid task id", StringComparison.OrdinalIgnoreCase) ? 400 : 502;
                    Console.Error.WriteLine($"Error approving task: {error}");
                    return Results.Json(new ErrorResponseDto { Error = message }, statusCode: status);
                }
            });

        app.MapPost(
            "/api/orch/tasks/{taskId}/reject",
            async (string taskId, HttpContext ctx) =>
            {
                try
                {
                    var body = await ReadOptionalJsonBodyAsync(ctx.Request);
                    var review = OrchestrationRequestParsers.ParseTaskReviewRequest(body);
                    var ok = await useCases.RejectTaskAsync(taskId, review.Reason);

                    if (!ok)
                        return Results.Json(new ErrorResponseDto { Error = "Task not found or not awaiting review" }, statusCode: 404);

                    return Results.Json(new HealthResponseDto { Ok = true });
                }
                catch (Exception error)
                {
                    var message = error.Message;
                    var status = message.Contains("Invalid task id", StringComparison.OrdinalIgnoreCase) ? 400 : 502;
                    Console.Error.WriteLine($"Error rejecting task: {error}");
                    return Results.Json(new ErrorResponseDto { Error = message }, statusCode: status);
                }
            });

        app.MapPost(
            "/api/orch/tasks/{taskId}/requeue",
            async (string taskId, HttpContext ctx) =>
            {
                try
                {
                    var body = await ReadOptionalJsonBodyAsync(ctx.Request);
                    var review = OrchestrationRequestParsers.ParseTaskReviewRequest(body);
                    var ok = await useCases.RequeueTaskAsync(taskId, review.Reason);

                    if (!ok)
                        return Results.Json(new ErrorResponseDto { Error = "Task not found or not requeueable" }, statusCode: 404);

                    return Results.Json(new HealthResponseDto { Ok = true });
                }
                catch (Exception error)
                {
                    var message = error.Message;
                    var status = message.Contains("Invalid task id", StringComparison.OrdinalIgnoreCase) ? 400 : 502;
                    Console.Error.WriteLine($"Error requeueing task: {error}");
                    return Results.Json(new ErrorResponseDto { Error = message }, statusCode: status);
                }
            });

        app.MapPost(
            "/api/orch/tasks/{taskId}/reprompt",
            async (string taskId, HttpContext ctx) =>
            {
                try
                {
                    var body = await ReadOptionalJsonBodyAsync(ctx.Request);
                    var review = OrchestrationRequestParsers.ParseTaskReviewRequest(body);
                    var ok = await useCases.RepromptTaskAsync(taskId, review.Instructions ?? string.Empty, review.Reason);

                    if (!ok)
                        return Results.Json(new ErrorResponseDto { Error = "Task not found or not repromptable" }, statusCode: 404);

                    return Results.Json(new HealthResponseDto { Ok = true });
                }
                catch (Exception error)
                {
                    var message = error.Message;
                    var status = message.Contains("Invalid task id", StringComparison.OrdinalIgnoreCase) || message.Contains("Missing instructions", StringComparison.OrdinalIgnoreCase) ? 400 : 502;
                    Console.Error.WriteLine($"Error reprompting task: {error}");
                    return Results.Json(new ErrorResponseDto { Error = message }, statusCode: status);
                }
            });

        app.MapGet(
            "/api/orch/tasks/{taskId}/review-history",
            async (string taskId, HttpContext ctx) =>
            {
                SetNoStore(ctx.Response);

                try
                {
                    var items = await useCases.GetTaskReviewHistoryAsync(taskId);

                    return Results.Json(
                        new TaskReviewHistoryListDto
                        {
                            Items = items
                        });
                }
                catch (Exception error)
                {
                    var message = error.Message;
                    var status = message.Contains("Invalid task id", StringComparison.OrdinalIgnoreCase) ? 400 : 502;
                    Console.Error.WriteLine($"Error loading task review history: {error}");
                    return Results.Json(new ErrorResponseDto { Error = message }, statusCode: status);
                }
            });

        app.MapPost(
            "/api/test/orch/reset",
            async () =>
            {
                try
                {
                    await useCases.ResetStateAsync();

                    return Results.Json(
                        new OrchestrationResetStateDto
                        {
                            Ok = true
                        });
                }
                catch (Exception error)
                {
                    Console.Error.WriteLine($"Error resetting orchestration state: {error}");

                    return Results.Json(
                        new ErrorResponseDto
                        {
                            Error = "Failed to reset orchestration state"
                        },
                        statusCode: 502);
                }
            });

        return app;
    }

    static void SetNoStore(HttpResponse response)
    {
        response.Headers.CacheControl = "no-store, no-cache, must-revalidate, private";
        response.Headers.Pragma = "no-cache";
        response.Headers.Expires = "0";
    }

    static async Task<string?> ReadOptionalJsonBodyAsync(HttpRequest request)
    {
        request.EnableBuffering();

        using var reader = new StreamReader(request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var body = await reader.ReadToEndAsync();
        request.Body.Position = 0;

        if (string.IsNullOrWhiteSpace(body))
            return null;

        return body;
    }
}
