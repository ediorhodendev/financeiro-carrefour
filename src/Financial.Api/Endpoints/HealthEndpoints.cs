using Financial.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
namespace Financial.Api.Endpoints;

public static class HealthEndpoints
{
    public static void MapHealthEndpoints(this WebApplication app)
    {
        app.MapGet("/health/live", () => Results.Ok(new { status = "alive" })).WithTags("Health");
        app.MapGet("/health/ready", async (FinancialDbContext db, CancellationToken ct) =>
        {
            // Readiness checks schema availability too, without returning financial data.
            await db.Accounts.Select(a => a.Id).Take(1).ToArrayAsync(ct);
            return Results.Ok(new { status = "ready" });
        }).WithTags("Health");
    }
}
