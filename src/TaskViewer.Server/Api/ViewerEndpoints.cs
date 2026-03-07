using Microsoft.AspNetCore.Mvc;
using TaskViewer.Server.Infrastructure.OpenCode;

namespace TaskViewer.Server.Api;

public static class ViewerEndpoints
{
    public static IEndpointRouteBuilder MapViewerEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(
            "/api/health",
            () => Results.Json(
                new HealthResponseDto
                {
                    Ok = true
                }));

        app.MapGet(
            "/api/tasks/all",
            async (HttpContext ctx, [FromServices] OpenCodeTasksOverviewService openCodeTasksOverview) =>
            {
                SetNoStore(ctx.Response);

                try
                {
                    var allTasks = await openCodeTasksOverview.GetAllTasksAsync();
                    return Results.Json(allTasks);
                }
                catch (Exception error)
                {
                    Console.Error.WriteLine($"Error getting all tasks: {error}");

                    return Results.Json(
                        new ErrorResponseDto
                        {
                            Error = "Failed to load tasks from OpenCode"
                        },
                        statusCode: 502);
                }
            });

        app.MapPost(
            "/api/tasks/{sessionId}/{taskId}/note",
            () => Results.Json(
                new ErrorResponseDto
                {
                    Error = "Not implemented for OpenCode todos"
                },
                statusCode: 501));

        app.MapDelete(
            "/api/tasks/{sessionId}/{taskId}",
            () => Results.Json(
                new ErrorResponseDto
                {
                    Error = "Not implemented for OpenCode todos"
                },
                statusCode: 501));

        app.MapGet(
            "/api/events",
            async (HttpContext ctx, [FromServices] SseHub sseHub) =>
            {
                ctx.Response.Headers.ContentType = "text/event-stream";
                ctx.Response.Headers.CacheControl = "no-cache";
                ctx.Response.Headers.Connection = "keep-alive";

                var client = sseHub.AddClient(ctx.Response, ctx.RequestAborted);

                await client.Send(
                    new ViewerEventDto
                    {
                        Type = "connected"
                    });

                await client.Completion;
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
