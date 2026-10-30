using Financial.Application.Movements;
namespace Financial.Api.Endpoints;

[System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
public sealed record ReversalRequest(string Reason);
public static class ReversalEndpoints
{
    public static void MapReversalEndpoints(this WebApplication app)
    {
        app.MapPost("/api/accounts/{accountId:guid}/movements/{movementId:guid}/reversal", async (
            Guid accountId, Guid movementId, ReversalRequest request, HttpContext context,
            ReverseMovementHandler handler, CancellationToken ct) =>
        {
            var subject = context.User.FindFirst("sub")?.Value;
            if (string.IsNullOrWhiteSpace(subject)) return Results.Unauthorized();
            if (!context.Request.Headers.TryGetValue("Idempotency-Key", out var keys) || keys.Count != 1)
                return Results.Problem(statusCode: 400, title: "Provide one Idempotency-Key header.");
            var result = await handler.ExecuteAsync(new(accountId, movementId, request.Reason, keys[0] ?? "", subject), ct);
            return Results.Json(result, statusCode: result.Replayed ? 200 : 201);
        }).RequireAuthorization(p => p.RequireRole("operator")).WithTags("Movements")
            .WithSummary("Reverse a movement in full, once, preserving the immutable original.")
            .WithDescription("A reason (1-500 trimmed characters) and Idempotency-Key are required. Credit reversals require sufficient funds; debit reversals respect the balance limit. Reversing a reversal is forbidden.")
            .Produces<ReversalResult>(201).Produces<ReversalResult>(200).ProducesProblem(400)
            .ProducesProblem(404).ProducesProblem(409).ProducesProblem(503);
    }
}
