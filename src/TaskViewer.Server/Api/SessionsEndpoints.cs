using TaskViewer.Domain.Sessions;

namespace TaskViewer.Server.Api;

public static class SessionsEndpoints
{
    public static IEndpointRouteBuilder MapSessionsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(
            "/api/tasks/board",
            async (HttpContext ctx, string? limit, ISessionsUseCases useCases, CancellationToken cancellationToken) =>
            {
                SetNoStore(ctx.Response);
                var items = await useCases.ListSessionsAsync(limit, cancellationToken);

                return Results.Json(items);
            })
            .WithApiExceptionHandling(
                "Error listing task board items",
                _ => ApiErrorResult.BadGateway("Failed to list tasks from orchestration"));

        app.MapGet(
            "/api/sessions",
            async (HttpContext ctx, string? limit, ISessionsUseCases useCases, CancellationToken cancellationToken) =>
            {
                SetNoStore(ctx.Response);
                var items = await useCases.ListSessionsAsync(limit, cancellationToken);

                return Results.Json(items);
            })
            .WithApiExceptionHandling(
                "Error listing sessions",
                _ => ApiErrorResult.BadGateway("Failed to list tasks from orchestration"));

        app.MapGet(
            "/api/sessions/{sessionId}",
            async (string sessionId, ISessionsUseCases useCases, CancellationToken cancellationToken) =>
            {
                var result = await useCases.GetSessionTasksAsync(sessionId, cancellationToken);

                if (!result.Found)
                    return Results.Json(
                        new ErrorResponseDto
                        {
                            Error = "Session not found"
                        },
                        statusCode: 404);

                return Results.Json(result.Tasks);
            })
            .WithApiExceptionHandling(
                "Error getting session todos",
                _ => ApiErrorResult.BadGateway("Failed to load session todos from OpenCode"));

        app.MapGet(
            "/api/sessions/{sessionId}/last-assistant-message",
            async (string sessionId, ISessionsUseCases useCases, CancellationToken cancellationToken) =>
            {
                var result = await useCases.GetLastAssistantMessageAsync(sessionId, cancellationToken);

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
            })
            .WithApiExceptionHandling(
                "Error getting last assistant message",
                _ => ApiErrorResult.BadGateway("Failed to load session messages from OpenCode"));

        app.MapGet(
            "/api/tasks/board/{taskId}/last-assistant-message",
            async (string taskId, ISessionsUseCases useCases, CancellationToken cancellationToken) =>
            {
                var result = await useCases.GetTaskLastAssistantMessageAsync(taskId, cancellationToken);

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
            })
            .WithApiExceptionHandling(
                "Error getting task last assistant message",
                _ => ApiErrorResult.BadGateway("Failed to load task messages from OpenCode"));

        app.MapPost(
            "/api/sessions/{sessionId}/archive",
            async (string sessionId, ISessionsUseCases useCases, CancellationToken cancellationToken) =>
            {
                var result = await useCases.ArchiveSessionAsync(sessionId, cancellationToken);

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
            })
            .WithApiExceptionHandling(
                "Error archiving session",
                _ => ApiErrorResult.BadGateway("Failed to archive session in OpenCode"));

        return app;
    }

    static void SetNoStore(HttpResponse response)
    {
        response.Headers.CacheControl = "no-store, no-cache, must-revalidate, private";
        response.Headers.Pragma = "no-cache";
        response.Headers.Expires = "0";
    }
}
