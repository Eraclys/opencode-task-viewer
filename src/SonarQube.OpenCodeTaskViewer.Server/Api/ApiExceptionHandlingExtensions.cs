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

public readonly record struct ApiErrorResult(string Message, int StatusCode)
{
    public static ApiErrorResult BadRequest(string message) => new(message, StatusCodes.Status400BadRequest);
    public static ApiErrorResult NotFound(string message) => new(message, StatusCodes.Status404NotFound);
    public static ApiErrorResult BadGateway(string message) => new(message, StatusCodes.Status502BadGateway);
}
