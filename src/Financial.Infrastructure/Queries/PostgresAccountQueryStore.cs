using Financial.Application.Queries;
using Financial.Domain.Movements;
using Financial.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
namespace Financial.Infrastructure.Queries;

public sealed class PostgresAccountQueryStore(IDbContextFactory<FinancialDbContext> contexts) : IAccountQueryStore
{
    public async Task<AccountBalanceSnapshot?> ReadBalanceAsync(Guid accountId, string subjectId, DateTimeOffset? at, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        var accounts = db.Accounts.AsNoTracking().Where(a => a.Id == accountId &&
            db.Customers.Any(c => c.Id == a.CustomerId && c.SubjectId == subjectId));
        // One SQL statement: account ownership and the aggregate share the same statement snapshot.
        if (at is null)
            return await accounts.Select(a => new AccountBalanceSnapshot(a.Id, a.Currency, a.Balance, a.CreatedAt)).SingleOrDefaultAsync(ct);
        return await accounts.Select(a => new AccountBalanceSnapshot(a.Id, a.Currency,
            db.Movements.Where(m => m.AccountId == a.Id && m.RegisteredAt <= at.Value)
                .Sum(m => (decimal?)(m.Type == MovementType.Credit ? m.Amount : -m.Amount)) ?? 0m,
            a.CreatedAt)).SingleOrDefaultAsync(ct);
    }
    public async Task<IReadOnlyList<MovementItem>?> ReadMovementsAsync(Guid accountId, string subjectId, MovementCursor? after, int take, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        if (!await db.Accounts.AnyAsync(a => a.Id == accountId && db.Customers.Any(c => c.Id == a.CustomerId && c.SubjectId == subjectId), ct)) return null;
        // Query ownership again in the data query. A cursor never grants access to an account.
        var query = db.Movements.AsNoTracking().Where(m => m.AccountId == accountId &&
            db.Accounts.Any(a => a.Id == m.AccountId && db.Customers.Any(c => c.Id == a.CustomerId && c.SubjectId == subjectId)));
        if (after is not null)
            query = query.Where(m => m.RegisteredAt > after.At || m.RegisteredAt == after.At && m.Id.CompareTo(after.Id) > 0);
        return await query.OrderBy(m => m.RegisteredAt).ThenBy(m => m.Id).Take(take)
            .Select(m => new MovementItem(m.Id, m.Type, m.Amount, m.RegisteredAt)).ToListAsync(ct);
    }
}
