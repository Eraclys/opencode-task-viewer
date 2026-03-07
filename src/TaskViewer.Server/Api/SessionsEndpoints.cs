using TaskViewer.Server.Application.Sessions;

namespace TaskViewer.Server.Api;

public static class SessionsEndpoints
{
    public static IEndpointRouteBuilder MapSessionsEndpoints(this IEndpointRouteBuilder app, ISessionsUseCases useCases)
    {
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
                    return Results.Json(new { error = "Failed to list sessions from OpenCode" }, statusCode: 502);
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
                        return Results.Json(new { error = "Session not found" }, statusCode: 404);

                    return Results.Json(result.Tasks);
                }
                catch (Exception error)
                {
                    Console.Error.WriteLine($"Error getting session todos: {error}");
                    return Results.Json(new { error = "Failed to load session todos from OpenCode" }, statusCode: 502);
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
                        return Results.Json(new { error = "Session not found" }, statusCode: 404);

                    return Results.Json(new
                    {
                        sessionId = result.SessionId,
                        message = result.Message,
                        createdAt = result.CreatedAt
                    });
                }
                catch (Exception error)
                {
                    Console.Error.WriteLine($"Error getting last assistant message: {error}");
                    return Results.Json(new { error = "Failed to load session messages from OpenCode" }, statusCode: 502);
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
                        return Results.Json(new { error = "Session not found" }, statusCode: 404);

                    return Results.Json(new
                    {
                        ok = true,
                        archivedAt = result.ArchivedAt
                    });
                }
                catch (Exception error)
                {
                    Console.Error.WriteLine($"Error archiving session: {error}");
                    return Results.Json(new { error = "Failed to archive session in OpenCode" }, statusCode: 502);
                }
            });

        return app;
    }

    private static void SetNoStore(HttpResponse response)
    {
        response.Headers.CacheControl = "no-store, no-cache, must-revalidate, private";
        response.Headers.Pragma = "no-cache";
        response.Headers.Expires = "0";
    }
}
