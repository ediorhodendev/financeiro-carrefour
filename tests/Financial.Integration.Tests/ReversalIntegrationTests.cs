using Financial.Application.Movements;
using Financial.Domain.Movements;
using Financial.Infrastructure.Movements;
using Financial.Infrastructure.Persistence;
using Financial.Infrastructure.Queries;
using Financial.Application.Queries;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;
namespace Financial.Integration.Tests;
public sealed partial class MovementIntegrationTests
{
    private static ReverseMovementHandler ReversalHandler(params Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor[] interceptors) =>
        new(new PostgresAccountTransactionFactory(new Factory(Options(interceptors))));
    [Fact] public async Task ReversalPreservesHistoryAndPublishesOneLinkedCompensation()
    {
        var account = await Seed(); var original = await Handler().ExecuteAsync(new(account.Id, MovementType.Credit, 100m, "reverse-source", account.Subject), default);
        var result = await ReversalHandler().ExecuteAsync(new(account.Id, original.Id, "Mistaken credit", "reverse-key", account.Subject), default);
        await using var db = new FinancialDbContext(Options());
        Assert.Equal(0m, (await db.Accounts.SingleAsync(a => a.Id == account.Id)).Balance);
        Assert.Equal(2, await db.Movements.CountAsync(m => m.AccountId == account.Id));
        Assert.Null((await db.Movements.SingleAsync(m => m.Id == original.Id)).ReversalOfMovementId);
        Assert.Equal(original.Id, (await db.Movements.SingleAsync(m => m.Id == result.Id)).ReversalOfMovementId);
        var payload = (await db.OutboxMessages.SingleAsync(m => m.Id == result.Id)).Payload;
        Assert.Contains(original.Id.ToString(), payload); Assert.DoesNotContain("Mistaken credit", payload);
        var queries = new AccountQueries(new PostgresAccountQueryStore(new Factory(Options())), TimeProvider.System);
        Assert.Equal(100m, (await queries.ExecuteAsync(account.Id, account.Subject, original.RegisteredAt, default)).Balance);
        var page = await queries.ListAsync(account.Id, account.Subject, 10, null, default);
        Assert.Equal(original.Id, page.Items.Single(m => m.Id == result.Id).ReversalOfMovementId);
        Assert.Equal("Mistaken credit", page.Items.Single(m => m.Id == result.Id).ReversalReason);
    }
    [Theory] [InlineData(true)] [InlineData(false)]
    public async Task ConcurrentReversalsCreateOneCompensation(bool sameKey)
    {
        var account = await Seed(); var original = await Handler().ExecuteAsync(new(account.Id, MovementType.Credit, 100m, "initial", account.Subject), default);
        async Task<ReversalResult?> Reverse(int i)
        {
            try { return await ReversalHandler().ExecuteAsync(new(account.Id, original.Id, "Duplicate booking", sameKey ? "same" : "reverse-" + i, account.Subject), default); }
            catch (MovementConflictException) { return null; }
        }
        var results = await Task.WhenAll(Enumerable.Range(0, 12).Select(Reverse));
        if (sameKey) { Assert.All(results, r => Assert.NotNull(r)); Assert.Single(results, r => !r!.Replayed); }
        else Assert.Single(results, r => r is not null);
        await using var db = new FinancialDbContext(Options());
        Assert.Equal(1, await db.Movements.CountAsync(m => m.ReversalOfMovementId == original.Id));
        Assert.Equal(0m, (await db.Accounts.SingleAsync(a => a.Id == account.Id)).Balance);
    }
    [Fact] public async Task FailedReversalRollsBackBalanceLedgerAndOutbox()
    {
        var account = await Seed(); var original = await Handler().ExecuteAsync(new(account.Id, MovementType.Credit, 100m, "initial", account.Subject), default);
        await Assert.ThrowsAsync<InjectedFailureException>(() => ReversalHandler(new FailAfterSave()).ExecuteAsync(new(account.Id, original.Id, "Rollback", "rollback", account.Subject), default));
        await using var db = new FinancialDbContext(Options());
        Assert.Equal(100m, (await db.Accounts.SingleAsync(a => a.Id == account.Id)).Balance);
        Assert.False(await db.Movements.AnyAsync(m => m.ReversalOfMovementId == original.Id));
        Assert.Equal(1, await db.OutboxMessages.CountAsync(m => db.Movements.Where(x => x.AccountId == account.Id).Select(x => x.Id).Contains(m.Id)));
        Assert.DoesNotContain(await db.OutboxMessages.ToListAsync(), m => m.Payload.Contains("\"ReversalOfMovementId\":\"" + original.Id, StringComparison.OrdinalIgnoreCase));
    }
    [Fact] public async Task OtherAccountMovementCannotBeReversed()
    {
        var owner = await Seed(); var other = await Seed();
        var movement = await Handler().ExecuteAsync(new(other.Id, MovementType.Credit, 1m, "other", other.Subject), default);
        await Assert.ThrowsAsync<MovementNotFoundException>(() => ReversalHandler().ExecuteAsync(new(owner.Id, movement.Id, "Wrong account", "key", owner.Subject), default));
    }
    [Fact] public async Task InsufficientReversalCanBeRetriedAfterFundsAreRestored()
    {
        var account = await Seed(); var original = await Handler().ExecuteAsync(new(account.Id, MovementType.Credit, 100m, "initial", account.Subject), default);
        await Handler().ExecuteAsync(new(account.Id, MovementType.Debit, 30m, "spend", account.Subject), default);
        var command = new ReverseMovementCommand(account.Id, original.Id, "Correction", "retry", account.Subject);
        await Assert.ThrowsAsync<MovementConflictException>(() => ReversalHandler().ExecuteAsync(command, default));
        await Handler().ExecuteAsync(new(account.Id, MovementType.Credit, 30m, "restore", account.Subject), default);
        Assert.False((await ReversalHandler().ExecuteAsync(command, default)).Replayed);
    }
    [Fact] public async Task DatabaseRejectsSecondReversalEvenWithoutHandler()
    {
        var account = await Seed(); var original = await Handler().ExecuteAsync(new(account.Id, MovementType.Credit, 10m, "initial", account.Subject), default);
        await ReversalHandler().ExecuteAsync(new(account.Id, original.Id, "Correction", "first", account.Subject), default);
        await using var db = new FinancialDbContext(Options());
        var entity = await db.Movements.AsNoTracking().SingleAsync(m => m.Id == original.Id);
        db.Movements.Add(entity.CreateReversal(DateTimeOffset.UtcNow, "second", ReverseMovementHandler.ComputeHash(original.Id, "Again"), "Again"));
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.UniqueViolation, Assert.IsType<PostgresException>(exception.InnerException).SqlState);
    }
    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public async Task DatabaseRejectsInvalidReversalMetadata(int violation)
    {
        var account = await Seed(); var original = await Handler().ExecuteAsync(new(account.Id, MovementType.Credit, 10m, "initial", account.Subject), default);
        var target = original.Id; var accountId = account.Id; var type = "Debit"; var amount = 10m;
        if (violation == 0) amount = 5m;
        if (violation == 1) type = "Credit";
        if (violation == 2) accountId = (await Seed()).Id;
        if (violation == 3)
        {
            target = (await ReversalHandler().ExecuteAsync(new(account.Id, original.Id, "Correction", "first", account.Subject), default)).Id;
            type = "Credit";
        }
        await using var db = new FinancialDbContext(Options());
        var id = Guid.NewGuid(); var key = id.ToString(); var hash = new string('A', 64); var now = DateTimeOffset.UtcNow;
        var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO movements ("Id", "AccountId", "Type", "Amount", "RegisteredAt", "IdempotencyKey", "RequestHash", "ReversalOfMovementId", "ReversalReason")
            VALUES ({id}, {accountId}, {type}, {amount}, {now}, {key}, {hash}, {target}, 'Correction')
            """));
        Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
        Assert.False(await db.Movements.AnyAsync(m => m.Id == id));
    }
}
