using System.Data;
using Financial.Application.Movements;
using Financial.Domain.Accounts;
using Financial.Domain.Movements;
using Financial.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
namespace Financial.Infrastructure.Movements;

public sealed class PostgresAccountTransactionFactory(IDbContextFactory<FinancialDbContext> contexts) : IAccountTransactionFactory
{
    public async Task<IAccountTransaction> BeginAsync(Guid accountId, string subjectId, CancellationToken ct)
    {
        var db = await contexts.CreateDbContextAsync(ct);
        try
        {
            var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
            await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '5s'; SET LOCAL statement_timeout = '10s';", ct);
            // Ownership is part of the locked query. SQL parameters are never concatenated.
            var accounts = await db.Accounts.FromSqlInterpolated($"""
                SELECT a.* FROM accounts a
                JOIN customers c ON c."Id" = a."CustomerId"
                WHERE a."Id" = {accountId} AND c."SubjectId" = {subjectId}
                FOR UPDATE OF a
                """).ToListAsync(ct);
            var account = accounts.SingleOrDefault() ?? throw new AccountNotFoundException();
            return new PostgresAccountTransaction(db, transaction, account);
        }
        catch
        {
            // Context disposal also rolls back an uncommitted transaction.
            await db.DisposeAsync();
            throw;
        }
    }
    private sealed class PostgresAccountTransaction(FinancialDbContext db, IDbContextTransaction transaction, Account account) : IAccountTransaction
    {
        public Account Account { get; } = account;
        public Task<Movement?> FindMovementAsync(string key, CancellationToken ct) =>
            db.Movements.AsNoTracking().SingleOrDefaultAsync(m => m.AccountId == Account.Id && m.IdempotencyKey == key, ct);
        public Task<Movement?> FindMovementByIdAsync(Guid id, CancellationToken ct) =>
            db.Movements.AsNoTracking().SingleOrDefaultAsync(m => m.AccountId == Account.Id && m.Id == id, ct);
        public Task<bool> HasReversalAsync(Guid id, CancellationToken ct) =>
            db.Movements.AnyAsync(m => m.AccountId == Account.Id && m.ReversalOfMovementId == id, ct);
        public async Task<DateTimeOffset> GetRegistrationTimeAsync(CancellationToken ct)
        {
            // PostgreSQL now() is the transaction start time, before waiting for the account lock.
            var values = await db.Database.SqlQuery<DateTimeOffset>($"SELECT clock_timestamp() AS \"Value\"").ToListAsync(ct);
            return values.Single();
        }
        public async Task CommitAsync(Movement movement, CancellationToken ct)
        {
            db.Movements.Add(movement);
            db.OutboxMessages.Add(new Financial.Infrastructure.Messaging.OutboxMessage(movement));
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        public async ValueTask DisposeAsync()
        {
            try { await transaction.DisposeAsync(); }
            finally { await db.DisposeAsync(); }
        }
    }
}
