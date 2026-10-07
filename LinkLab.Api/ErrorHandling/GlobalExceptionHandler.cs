using Microsoft.AspNetCore.Diagnostics;

namespace LinkLab.Api.ErrorHandling;

public class GlobalExceptionHandler(
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext context,
        Exception exception,
        CancellationToken cancellationToken)
    {
        logger.LogError(
            exception,
            "Unhandled error while processing {Method} {Path}",
            context.Request.Method,
            context.Request.Path);

        context.Response.StatusCode =
            StatusCodes.Status500InternalServerError;

        await context.Response.WriteAsJsonAsync(
            new { error = "An unexpected error occurred. Please try again later." },
            cancellationToken);

        return true;
    }
}
