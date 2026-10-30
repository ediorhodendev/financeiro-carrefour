using Financial.Application.Movements;
using Financial.Domain.Movements;
namespace Financial.Api.Endpoints;

public static class MovementEndpoints
{
    public static void MapMovementEndpoints(this WebApplication app)
    {
        app.MapPost("/api/accounts/{accountId:guid}/movements", async (
            Guid accountId, MovementRequest request, HttpContext context, RegisterMovementHandler handler, CancellationToken ct) =>
        {
            var subject = context.User.FindFirst("sub")?.Value;
            if (string.IsNullOrWhiteSpace(subject)) return Results.Unauthorized();
            if (!context.Request.Headers.TryGetValue("Idempotency-Key", out var keys) || keys.Count != 1)
                return Results.Problem(statusCode: 400, title: "Provide one Idempotency-Key header.");
            var result = await handler.ExecuteAsync(new RegisterMovementCommand(accountId, request.Type, request.Amount, keys[0] ?? "", subject), ct);
            return Results.Json(result, statusCode: result.Replayed ? 200 : 201);
        }).RequireAuthorization(policy => policy.RequireRole("operator")).WithTags("Movements")
            .WithSummary("Register a credit or debit atomically; retries reuse Idempotency-Key.")
            .Produces<MovementResult>(201).Produces<MovementResult>(200).ProducesProblem(400)
            .ProducesProblem(404).ProducesProblem(409).ProducesProblem(503);
    }
}

public sealed record MovementRequest(MovementType Type, decimal Amount);
