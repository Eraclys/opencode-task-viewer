namespace SonarQube.OpenCodeTaskViewer.Server.Api;

public readonly record struct ApiErrorResult(string Message, int StatusCode)
{
    public static ApiErrorResult BadRequest(string message) => new(message, StatusCodes.Status400BadRequest);
    public static ApiErrorResult NotFound(string message) => new(message, StatusCodes.Status404NotFound);
    public static ApiErrorResult BadGateway(string message) => new(message, StatusCodes.Status502BadGateway);
}