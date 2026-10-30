using Financial.Application.Queries;
using Financial.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
namespace Financial.Api.Endpoints;

public static class ReceiptEndpoints
{
    public static void MapReceiptEndpoints(this WebApplication app)
    {
        app.MapGet("/api/accounts/{accountId:guid}/receipts", async (Guid accountId, HttpContext context,
            FinancialDbContext db, AccountQueries queries, CancellationToken ct) =>
        {
            var subject = context.User.FindFirst("sub")?.Value;
            if (string.IsNullOrWhiteSpace(subject)) return Results.Unauthorized();
            await queries.ExecuteAsync(accountId, subject, null, ct);
            var items = await db.ProcessingReceipts.AsNoTracking().Where(r => r.AccountId == accountId)
                .OrderByDescending(r => r.ProcessedAt).Take(100).ToListAsync(ct);
            return Results.Ok(items);
        }).RequireAuthorization(policy => policy.RequireRole("operator")).WithTags("Messaging")
            .WithSummary("Latest 100 idempotent consumer receipts; eventually consistent.");
    }
}
