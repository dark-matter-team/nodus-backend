using Microsoft.AspNetCore.Diagnostics;
using Nodus.Api.Exceptions;

namespace Nodus.Api.Middleware;

public sealed class GlobalExceptionHandler(
    ILogger<GlobalExceptionHandler> logger,
    IHostEnvironment environment) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var (statusCode, message) = exception switch
        {
            ValidationException ex => (StatusCodes.Status400BadRequest, ex.Message),
            UnauthorizedException ex => (StatusCodes.Status401Unauthorized, ex.Message),
            ForbiddenException ex => (StatusCodes.Status403Forbidden, ex.Message),
            NotFoundException ex => (StatusCodes.Status404NotFound, ex.Message),
            ConflictException ex => (StatusCodes.Status409Conflict, ex.Message),
            ServiceUnavailableException ex => (StatusCodes.Status503ServiceUnavailable, ex.Message),
            _ => (
                StatusCodes.Status500InternalServerError,
                environment.IsDevelopment() ? exception.Message : "An unexpected error occurred."
            )
        };

        if (statusCode == StatusCodes.Status500InternalServerError)
        {
            logger.LogError(
                exception,
                "Unhandled exception occurred while processing {Method} {Path}",
                httpContext.Request.Method,
                httpContext.Request.Path);
        }

        httpContext.Response.StatusCode = statusCode;
        await httpContext.Response.WriteAsJsonAsync(new { error = message }, cancellationToken);

        return true;
    }
}