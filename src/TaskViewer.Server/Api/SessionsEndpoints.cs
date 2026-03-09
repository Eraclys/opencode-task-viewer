using TaskViewer.Application.Sessions;

namespace TaskViewer.Server.Api;

public static class SessionsEndpoints
{
    public static IEndpointRouteBuilder MapSessionsEndpoints(this IEndpointRouteBuilder app, ISessionsUseCases useCases)
    {
        app.MapGet(
            "/api/tasks/board",
            async (HttpContext ctx) =>
            {
                SetNoStore(ctx.Response);

                try
                {
                    var items = await useCases.ListSessionsAsync(ctx.Request.Query["limit"].ToString());

                    return Results.Json(items);
                }
                catch (Exception error)
                {
                    Console.Error.WriteLine($"Error listing task board items: {error}");

                    return Results.Json(
                        new ErrorResponseDto
                        {
                            Error = "Failed to list tasks from orchestration"
                        },
                        statusCode: 502);
                }
            });

        app.MapGet(
            "/api/sessions",
            async (HttpContext ctx) =>
            {
                SetNoStore(ctx.Response);

                try
                {
                    var items = await useCases.ListSessionsAsync(ctx.Request.Query["limit"].ToString());

                    return Results.Json(items);
                }
                catch (Exception error)
                {
                    Console.Error.WriteLine($"Error listing sessions: {error}");

                    return Results.Json(
                        new ErrorResponseDto
                        {
                            Error = "Failed to list tasks from orchestration"
                        },
                        statusCode: 502);
                }
            });

        app.MapGet(
            "/api/sessions/{sessionId}",
            async (string sessionId) =>
            {
                try
                {
                    var result = await useCases.GetSessionTasksAsync(sessionId);

                    if (!result.Found)
                        return Results.Json(
                            new ErrorResponseDto
                            {
                                Error = "Session not found"
                            },
                            statusCode: 404);

                    return Results.Json(result.Tasks);
                }
                catch (Exception error)
                {
                    Console.Error.WriteLine($"Error getting session todos: {error}");

                    return Results.Json(
                        new ErrorResponseDto
                        {
                            Error = "Failed to load session todos from OpenCode"
                        },
                        statusCode: 502);
                }
            });

        app.MapGet(
            "/api/sessions/{sessionId}/last-assistant-message",
            async (string sessionId) =>
            {
                try
                {
                    var result = await useCases.GetLastAssistantMessageAsync(sessionId);

                    if (!result.Found)
                        return Results.Json(
                            new ErrorResponseDto
                            {
                                Error = "Session not found"
                            },
                            statusCode: 404);

                    return Results.Json(
                        new LastAssistantMessageResponseDto
                        {
                            SessionId = result.SessionId,
                            Message = result.Message,
                            CreatedAt = result.CreatedAt
                        });
                }
                catch (Exception error)
                {
                    Console.Error.WriteLine($"Error getting last assistant message: {error}");

                    return Results.Json(
                        new ErrorResponseDto
                        {
                            Error = "Failed to load session messages from OpenCode"
                        },
                        statusCode: 502);
                }
            });

        app.MapGet(
            "/api/tasks/board/{taskId}/last-assistant-message",
            async (string taskId) =>
            {
                try
                {
                    var result = await useCases.GetTaskLastAssistantMessageAsync(taskId);

                    if (!result.Found)
                        return Results.Json(
                            new ErrorResponseDto
                            {
                                Error = "Task not found"
                            },
                            statusCode: 404);

                    return Results.Json(
                        new LastAssistantMessageResponseDto
                        {
                            SessionId = result.SessionId,
                            Message = result.Message,
                            CreatedAt = result.CreatedAt
                        });
                }
                catch (Exception error)
                {
                    Console.Error.WriteLine($"Error getting task last assistant message: {error}");

                    return Results.Json(
                        new ErrorResponseDto
                        {
                            Error = "Failed to load task messages from OpenCode"
                        },
                        statusCode: 502);
                }
            });

        app.MapPost(
            "/api/sessions/{sessionId}/archive",
            async (string sessionId) =>
            {
                try
                {
                    var result = await useCases.ArchiveSessionAsync(sessionId);

                    if (!result.Found)
                        return Results.Json(
                            new ErrorResponseDto
                            {
                                Error = "Session not found"
                            },
                            statusCode: 404);

                    return Results.Json(
                        new ArchiveSessionResponseDto
                        {
                            Ok = true,
                            ArchivedAt = result.ArchivedAt
                        });
                }
                catch (Exception error)
                {
                    Console.Error.WriteLine($"Error archiving session: {error}");

                    return Results.Json(
                        new ErrorResponseDto
                        {
                            Error = "Failed to archive session in OpenCode"
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
}
