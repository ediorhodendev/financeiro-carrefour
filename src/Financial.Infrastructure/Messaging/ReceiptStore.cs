using Financial.Application.Messaging;
using Financial.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
namespace Financial.Infrastructure.Messaging;

public sealed class ReceiptStore(IDbContextFactory<FinancialDbContext> contexts)
{
    public async Task RecordAsync(MovementRecorded message, CancellationToken ct)
    {
        message.Validate();
        await using var db = await contexts.CreateDbContextAsync(ct);
        // A single INSERT makes deduplication and the effect atomic across consumers.
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO processing_receipts ("EventId", "MovementId", "AccountId", "ProcessedAt")
            VALUES ({message.EventId}, {message.MovementId}, {message.AccountId}, {DateTimeOffset.UtcNow})
            ON CONFLICT ("EventId") DO NOTHING
            """, ct);
    }
}
