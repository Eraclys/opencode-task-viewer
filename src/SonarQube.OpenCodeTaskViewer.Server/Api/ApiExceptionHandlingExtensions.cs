namespace SonarQube.OpenCodeTaskViewer.Server.Api;

public static class ApiExceptionHandlingExtensions
{
    public static RouteHandlerBuilder WithApiExceptionHandling(
        this RouteHandlerBuilder builder,
        string logMessage,
        Func<Exception, ApiErrorResult> mapError)
    {
        return builder.AddEndpointFilterFactory((_, next) => async invocationContext =>
        {
            try
            {
                return await next(invocationContext);
            }
            catch (Exception error)
            {
                var loggerFactory = invocationContext.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>();
                var logger = loggerFactory.CreateLogger("SonarQube.OpenCodeTaskViewer.Server.Api");
                logger.LogError(error, "{Message}", logMessage);

                var mapped = mapError(error);

                return Results.Json(
                    new ErrorResponseDto
                    {
                        Error = mapped.Message
                    },
                    statusCode: mapped.StatusCode);
            }
        });
    }
}
