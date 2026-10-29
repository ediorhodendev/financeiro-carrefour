using Financial.Application.Provisioning;
using Financial.Infrastructure.Provisioning;
using Npgsql;
using Financial.Application.Queries;
using Financial.Infrastructure.Queries;
using Financial.Application.Movements;
using Financial.Domain.Accounts;
using Financial.Domain.Customers;
using Financial.Domain.Movements;
using Financial.Infrastructure.Movements;
using Financial.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;
namespace Financial.Integration.Tests;
public sealed class MovementIntegrationTests
{
    private static DbContextOptions<FinancialDbContext> Options(params IInterceptor[] interceptors)
    {
        var connection = Environment.GetEnvironmentVariable("FINANCIAL_TEST_CONNECTION")
            ?? throw new InvalidOperationException("Set FINANCIAL_TEST_CONNECTION to an isolated PostgreSQL test database.");
        return new DbContextOptionsBuilder<FinancialDbContext>().UseNpgsql(connection).AddInterceptors(interceptors).Options;
    }
    private sealed class Factory(DbContextOptions<FinancialDbContext> options) : IDbContextFactory<FinancialDbContext>
    { public FinancialDbContext CreateDbContext() => new(options); }
    private static RegisterMovementHandler Handler(params IInterceptor[] interceptors) => new(new PostgresAccountTransactionFactory(new Factory(Options(interceptors))));
    private static async Task<(Guid Id, string Subject)> Seed()
    {
        await using var db = new FinancialDbContext(Options());
        await db.Database.MigrateAsync();
        var subject = "integration-" + Guid.NewGuid();
        var customer = new Customer("Fictitious customer", subject, DateTimeOffset.UtcNow);
        var account = new Account(customer.Id, DateTimeOffset.UtcNow);
        db.Add(customer); db.Add(account); await db.SaveChangesAsync();
        return (account.Id, subject);
    }
    [Fact] public async Task ConcurrentDuplicateCreditsCreateExactlyOneMovement()
    {
        var account = await Seed();
        var command = new RegisterMovementCommand(account.Id, MovementType.Credit, 100m, "duplicate", account.Subject);
        var results = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => Handler().ExecuteAsync(command, default)));
        Assert.Single(results, r => !r.Replayed); Assert.Single(results.Select(r => r.Id).Distinct());
        await using var db = new FinancialDbContext(Options());
        Assert.Equal(100m, (await db.Accounts.SingleAsync(a => a.Id == account.Id)).Balance);
        Assert.Equal(1, await db.Movements.CountAsync(m => m.AccountId == account.Id));
    }
    [Fact] public async Task ConcurrentDebitsCannotSpendSameBalance()
    {
        var account = await Seed();
        await Handler().ExecuteAsync(new(account.Id, MovementType.Credit, 100m, "initial", account.Subject), default);
        async Task<bool> Debit(string key)
        {
            try { await Handler().ExecuteAsync(new(account.Id, MovementType.Debit, 80m, key, account.Subject), default); return true; }
            catch (MovementConflictException) { return false; }
        }
        var results = await Task.WhenAll(Debit("one"), Debit("two"));
        Assert.Single(results, success => success);
        await using var db = new FinancialDbContext(Options());
        Assert.Equal(20m, (await db.Accounts.SingleAsync(a => a.Id == account.Id)).Balance);
        Assert.Equal(2, await db.Movements.CountAsync(m => m.AccountId == account.Id));
    }
    [Fact] public async Task DifferentPayloadForSameKeyIsRejected()
    {
        var account = await Seed(); var command = new RegisterMovementCommand(account.Id, MovementType.Credit, 10m, "same", account.Subject);
        await Handler().ExecuteAsync(command, default);
        await Assert.ThrowsAsync<MovementConflictException>(() => Handler().ExecuteAsync(command with { Amount = 11m }, default));
    }
    [Fact] public async Task OwnerMismatchCannotWrite()
    {
        var account = await Seed();
        await Assert.ThrowsAsync<AccountNotFoundException>(() => Handler().ExecuteAsync(new(account.Id, MovementType.Credit, 10m, "key", "other-owner"), default));
    }
    [Fact] public async Task FailureAfterSaveBeforeCommitRollsBackBothWrites()
    {
        var account = await Seed();
        await Assert.ThrowsAsync<InjectedFailureException>(() => Handler(new FailAfterSave()).ExecuteAsync(new(account.Id, MovementType.Credit, 10m, "rollback", account.Subject), default));
        await using var db = new FinancialDbContext(Options());
        Assert.Equal(0m, (await db.Accounts.SingleAsync(a => a.Id == account.Id)).Balance);
        Assert.Equal(0, await db.Movements.CountAsync(m => m.AccountId == account.Id));
        Assert.DoesNotContain(await db.OutboxMessages.ToListAsync(), m => m.Payload.Contains(account.Id.ToString(), StringComparison.OrdinalIgnoreCase));
    }
    [Fact] public async Task HistoricalBalanceIsInclusiveAndIgnoresLaterMovements()
    {
        var account = await Seed();
        var first = await Handler().ExecuteAsync(new(account.Id, MovementType.Credit, 100m, "historical-credit", account.Subject), default);
        await Handler().ExecuteAsync(new(account.Id, MovementType.Debit, 30m, "historical-debit", account.Subject), default);
        var queries = new AccountQueries(new PostgresAccountQueryStore(new Factory(Options())), TimeProvider.System);
        var past = await queries.ExecuteAsync(account.Id, account.Subject, first.RegisteredAt, default);
        var current = await queries.ExecuteAsync(account.Id, account.Subject, null, default);
        Assert.Equal(100m, past.Balance); Assert.Equal(70m, current.Balance);
    }
    [Fact] public async Task EmptyHistoryReturnsZeroAndOtherOwnerCannotRead()
    {
        var account = await Seed();
        var queries = new AccountQueries(new PostgresAccountQueryStore(new Factory(Options())), TimeProvider.System);
        var balance = await queries.ExecuteAsync(account.Id, account.Subject, DateTimeOffset.UtcNow, default);
        Assert.Equal(0m, balance.Balance);
        await Assert.ThrowsAsync<AccountNotFoundException>(() => queries.ExecuteAsync(account.Id, "wrong-owner", null, default));
        await Assert.ThrowsAsync<AccountNotFoundException>(() => queries.ListAsync(account.Id, "wrong-owner", 10, null, default));
    }
    [Fact] public async Task PagesWithEqualTimestampsHaveNoDuplicatesOrOmissions()
    {
        var account = await Seed();
        var instant = DateTimeOffset.UtcNow;
        await using (var db = new FinancialDbContext(Options()))
        {
            for (var i = 0; i < 5; i++)
                db.Movements.Add(new Movement(account.Id, MovementType.Credit, 1m, instant, "page-" + i, RegisterMovementHandler.ComputeHash(MovementType.Credit, 1m)));
            var entity = await db.Accounts.SingleAsync(a => a.Id == account.Id); entity.Credit(5m);
            await db.SaveChangesAsync();
        }
        var queries = new AccountQueries(new PostgresAccountQueryStore(new Factory(Options())), TimeProvider.System);
        var ids = new List<Guid>(); string? cursor = null;
        do
        {
            var page = await queries.ListAsync(account.Id, account.Subject, 2, cursor, default);
            ids.AddRange(page.Items.Select(m => m.Id)); cursor = page.NextCursor;
        } while (cursor is not null);
        Assert.Equal(5, ids.Count); Assert.Equal(5, ids.Distinct().Count());
    }
    [Fact] public async Task ConcurrentAccountOpeningsCreateOnlyOneAccount()
    {
        var store = new PostgresProvisioningStore(new Factory(Options()));
        await using (var db = new FinancialDbContext(Options())) { await db.Database.MigrateAsync(); }
        var handler = new ProvisioningHandler(store, TimeProvider.System);
        var customer = await handler.CreateCustomerAsync("Test", "provision-" + Guid.NewGuid(), default);
        async Task<bool> Open()
        {
            try { await handler.OpenAccountAsync(customer.Id, default); return true; }
            catch (ProvisioningConflictException) { return false; }
        }
        var results = await Task.WhenAll(Open(), Open());
        Assert.Single(results, success => success);
        await using var verify = new FinancialDbContext(Options());
        Assert.Equal(1, await verify.Accounts.CountAsync(a => a.CustomerId == customer.Id));
    }
    [Fact] public async Task DuplicateCustomerIdentityIsRejected()
    {
        await using (var db = new FinancialDbContext(Options())) { await db.Database.MigrateAsync(); }
        var handler = new ProvisioningHandler(new PostgresProvisioningStore(new Factory(Options())), TimeProvider.System);
        var subject = "duplicate-" + Guid.NewGuid();
        await handler.CreateCustomerAsync("One", subject, default);
        await Assert.ThrowsAsync<ProvisioningConflictException>(() => handler.CreateCustomerAsync("Two", subject, default));
    }
    [Fact] public async Task ConcurrentUniqueCreditsDoNotLoseUpdates()
    {
        var account = await Seed();
        await Task.WhenAll(Enumerable.Range(0, 30).Select(i => Handler().ExecuteAsync(new(account.Id, MovementType.Credit, 1m, "unique-" + i, account.Subject), default)));
        await using var db = new FinancialDbContext(Options());
        Assert.Equal(30m, (await db.Accounts.SingleAsync(a => a.Id == account.Id)).Balance);
        Assert.Equal(30, await db.Movements.CountAsync(m => m.AccountId == account.Id));
    }
    [Fact] public async Task DatabaseRejectsLedgerUpdateAndDelete()
    {
        var account = await Seed();
        var result = await Handler().ExecuteAsync(new(account.Id, MovementType.Credit, 5m, "immutable", account.Subject), default);
        await using var db = new FinancialDbContext(Options());
        var update = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE movements SET \"Amount\" = 6 WHERE \"Id\" = {result.Id}"));
        Assert.Equal(PostgresErrorCodes.CheckViolation, update.SqlState);
        var delete = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM movements WHERE \"Id\" = {result.Id}"));
        Assert.Equal(PostgresErrorCodes.CheckViolation, delete.SqlState);
        Assert.Equal(1, await db.Movements.CountAsync(m => m.Id == result.Id));
    }
    [Fact] public async Task MovementAndOutboxAreAtomicAndReplayDoesNotPublishTwice()
    {
        var account = await Seed();
        var command = new RegisterMovementCommand(account.Id, MovementType.Credit, 10m, "outbox", account.Subject);
        var result = await Handler().ExecuteAsync(command, default);
        await Handler().ExecuteAsync(command, default);
        await using var db = new FinancialDbContext(Options());
        var messages = await db.OutboxMessages.Where(m => m.Id == result.Id).ToListAsync();
        var item = Assert.Single(messages); Assert.Null(item.PublishedAt);
        var message = System.Text.Json.JsonSerializer.Deserialize<Financial.Application.Messaging.MovementRecorded>(item.Payload)!;
        Assert.Equal(account.Id, message.AccountId); Assert.Equal(result.Id, message.EventId);
    }
    [Fact] public async Task ConcurrentConsumerRedeliveryCreatesOneReceipt()
    {
        var account = await Seed();
        var result = await Handler().ExecuteAsync(new(account.Id, MovementType.Credit, 1m, "receipt", account.Subject), default);
        var message = new Financial.Application.Messaging.MovementRecorded(1, result.Id, result.Id, account.Id,
            MovementType.Credit, 1m, result.RegisteredAt);
        var store = new Financial.Infrastructure.Messaging.ReceiptStore(new Factory(Options()));
        await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => store.RecordAsync(message, default)));
        await using var db = new FinancialDbContext(Options());
        Assert.Equal(1, await db.ProcessingReceipts.CountAsync(r => r.EventId == result.Id));
        Assert.Equal(1m, (await db.Accounts.SingleAsync(a => a.Id == account.Id)).Balance);
    }
    private sealed class InjectedFailureException() : Exception("Injected failure before commit.");
    private sealed class FailAfterSave : SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
            => throw new InjectedFailureException();
    }
}
