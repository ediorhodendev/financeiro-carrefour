using Financial.Application.Movements;
using Financial.Application.Provisioning;
using Microsoft.AspNetCore.Diagnostics;
using Npgsql;
namespace Financial.Api.Errors;
public sealed class ApiExceptionHandler(IProblemDetailsService problems) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        var (status, title) = exception switch
        {
            BadHttpRequestException bad => (bad.StatusCode, "Invalid request."),
            ArgumentException => (400, "Invalid request."),
            AccountNotFoundException or CustomerNotFoundException => (404, "Resource not found."),
            MovementConflictException or ProvisioningConflictException => (409, exception.Message),
            _ when IsDatabaseUnavailable(exception) => (503, "Database temporarily unavailable. For movements retry with the same idempotency key."),
            _ => (500, "An unexpected error occurred.")
        };
        context.Response.StatusCode = status;
        await problems.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = new() { Status = status, Title = title, Extensions = { ["traceId"] = context.TraceIdentifier } }
        });
        return true;
    }
    private static bool IsDatabaseUnavailable(Exception exception) => exception is NpgsqlException or TimeoutException ||
        exception.InnerException is not null && IsDatabaseUnavailable(exception.InnerException);
}
