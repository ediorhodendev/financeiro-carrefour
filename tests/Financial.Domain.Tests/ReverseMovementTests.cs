using Financial.Application.Movements;
using Financial.Domain.Accounts;
using Financial.Domain.Movements;
using Xunit;
namespace Financial.Domain.Tests;

public sealed class ReverseMovementTests
{
    private sealed class Store : IAccountTransactionFactory, IAccountTransaction
    {
        public Account Account { get; } = new(Guid.NewGuid(), DateTimeOffset.UtcNow);
        public List<Movement> Movements { get; } = [];
        public int Commits { get; private set; }
        public bool Opened { get; private set; }
        public bool Disposed { get; private set; }
        public Movement Seed(MovementType type, decimal amount)
        {
            if (type == MovementType.Credit) Account.Credit(amount); else Account.Debit(amount);
            var movement = new Movement(Account.Id, type, amount, DateTimeOffset.UtcNow,
                Guid.NewGuid().ToString(), RegisterMovementHandler.ComputeHash(type, amount));
            Movements.Add(movement); return movement;
        }
        public ReverseMovementCommand Command(Movement original) => new(Account.Id, original.Id, "Incorrect booking", "reversal-001", "owner");
        public Task<IAccountTransaction> BeginAsync(Guid accountId, string subject, CancellationToken ct)
        { Opened = true; if (accountId != Account.Id || subject != "owner") throw new AccountNotFoundException(); return Task.FromResult<IAccountTransaction>(this); }
        public Task<Movement?> FindMovementAsync(string key, CancellationToken ct) => Task.FromResult(Movements.SingleOrDefault(m => m.IdempotencyKey == key));
        public Task<Movement?> FindMovementByIdAsync(Guid id, CancellationToken ct) => Task.FromResult(Movements.SingleOrDefault(m => m.Id == id));
        public Task<bool> HasReversalAsync(Guid id, CancellationToken ct) => Task.FromResult(Movements.Any(m => m.ReversalOfMovementId == id));
        public Task<DateTimeOffset> GetRegistrationTimeAsync(CancellationToken ct) => Task.FromResult(DateTimeOffset.UtcNow);
        public Task CommitAsync(Movement movement, CancellationToken ct) { Movements.Add(movement); Commits++; return Task.CompletedTask; }
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }
    [Fact] public async Task ReverseCreditCreatesLinkedDebitAndKeepsOriginal()
    {
        var store = new Store(); var original = store.Seed(MovementType.Credit, 100m);
        var result = await new ReverseMovementHandler(store).ExecuteAsync(store.Command(original), default);
        Assert.Equal(MovementType.Debit, result.Type); Assert.Equal(100m, result.Amount);
        Assert.Equal(original.Id, result.ReversalOfMovementId); Assert.Equal(0m, store.Account.Balance);
        Assert.Null(original.ReversalOfMovementId); Assert.Equal(2, store.Movements.Count);
        Assert.False(result.Replayed); Assert.True(store.Disposed);
    }
    [Fact] public async Task ReverseDebitRestoresFunds()
    {
        var store = new Store(); store.Seed(MovementType.Credit, 100m); var original = store.Seed(MovementType.Debit, 30m);
        var result = await new ReverseMovementHandler(store).ExecuteAsync(store.Command(original), default);
        Assert.Equal(MovementType.Credit, result.Type); Assert.Equal(100m, store.Account.Balance);
    }
    [Fact] public async Task SameRequestReplaysEvenWhenBalanceWouldNoLongerPermitDebit()
    {
        var store = new Store(); var original = store.Seed(MovementType.Credit, 100m); var handler = new ReverseMovementHandler(store);
        var command = store.Command(original);
        var first = await handler.ExecuteAsync(command, default); var second = await handler.ExecuteAsync(command, default);
        Assert.Equal(first.Id, second.Id); Assert.Equal(first.RegisteredAt, second.RegisteredAt);
        Assert.True(second.Replayed); Assert.Equal(1, store.Commits); Assert.Equal(0m, store.Account.Balance);
    }
    [Fact] public async Task ReasonIsTrimmedAndWhitespaceEquivalentReplayIsAccepted()
    {
        var store = new Store(); var original = store.Seed(MovementType.Credit, 1m); var handler = new ReverseMovementHandler(store);
        var command = store.Command(original);
        var first = await handler.ExecuteAsync(command with { Reason = "  Incorrect booking  " }, default);
        Assert.Equal("Incorrect booking", first.Reason);
        Assert.True((await handler.ExecuteAsync(command, default)).Replayed);
    }
    [Theory] [InlineData("")] [InlineData("   ")] [InlineData(null)]
    public async Task BlankReasonIsRejectedBeforeTransaction(string? reason)
    {
        var store = new Store(); var original = store.Seed(MovementType.Credit, 1m);
        await Assert.ThrowsAsync<ArgumentException>(() => new ReverseMovementHandler(store).ExecuteAsync(store.Command(original) with { Reason = reason! }, default));
        Assert.False(store.Opened);
    }
    [Fact] public async Task ReasonLongerThan500CharactersIsRejected()
    {
        var store = new Store(); var original = store.Seed(MovementType.Credit, 1m);
        await Assert.ThrowsAsync<ArgumentException>(() => new ReverseMovementHandler(store).ExecuteAsync(store.Command(original) with { Reason = new string('x', 501) }, default));
        Assert.False(store.Opened);
    }
    [Fact] public async Task ReasonWith500CharactersIsAccepted()
    {
        var store = new Store(); var original = store.Seed(MovementType.Credit, 1m);
        var result = await new ReverseMovementHandler(store).ExecuteAsync(store.Command(original) with { Reason = new string('x', 500) }, default);
        Assert.Equal(500, result.Reason.Length);
    }
    [Fact] public async Task SecondKeyCannotReverseSameMovementAgain()
    {
        var store = new Store(); var original = store.Seed(MovementType.Credit, 1m); var handler = new ReverseMovementHandler(store);
        var command = store.Command(original); await handler.ExecuteAsync(command, default);
        await Assert.ThrowsAsync<MovementConflictException>(() => handler.ExecuteAsync(command with { Key = "second" }, default));
        Assert.Equal(1, store.Commits);
    }
    [Fact] public async Task ExistingKeyCannotChangeReason()
    {
        var store = new Store(); var original = store.Seed(MovementType.Credit, 1m); var handler = new ReverseMovementHandler(store);
        var command = store.Command(original); await handler.ExecuteAsync(command, default);
        await Assert.ThrowsAsync<MovementConflictException>(() => handler.ExecuteAsync(command with { Reason = "Another reason" }, default));
    }
    [Fact] public async Task ReversalCannotBeReversed()
    {
        var store = new Store(); var original = store.Seed(MovementType.Credit, 1m); var handler = new ReverseMovementHandler(store);
        var result = await handler.ExecuteAsync(store.Command(original), default);
        await Assert.ThrowsAsync<MovementConflictException>(() => handler.ExecuteAsync(new(store.Account.Id, result.Id, "Undo", "new-key", "owner"), default));
        Assert.Equal(0m, store.Account.Balance);
    }
    [Fact] public async Task MissingMovementReturnsNotFound()
    {
        var store = new Store();
        await Assert.ThrowsAsync<MovementNotFoundException>(() => new ReverseMovementHandler(store).ExecuteAsync(new(store.Account.Id, Guid.NewGuid(), "Missing", "key", "owner"), default));
        Assert.Equal(0, store.Commits);
    }
    [Fact] public async Task WrongOwnerCannotReverse()
    {
        var store = new Store(); var original = store.Seed(MovementType.Credit, 1m);
        await Assert.ThrowsAsync<AccountNotFoundException>(() => new ReverseMovementHandler(store).ExecuteAsync(store.Command(original) with { SubjectId = "other" }, default));
        Assert.Equal(1m, store.Account.Balance);
    }
    [Fact] public async Task InsufficientFundsDoNotCommit()
    {
        var store = new Store(); var original = store.Seed(MovementType.Credit, 100m); store.Seed(MovementType.Debit, 30m);
        await Assert.ThrowsAsync<MovementConflictException>(() => new ReverseMovementHandler(store).ExecuteAsync(store.Command(original), default));
        Assert.Equal(70m, store.Account.Balance); Assert.Equal(0, store.Commits);
    }
    [Fact] public async Task RestoringDebitCannotExceedMaximumBalance()
    {
        var store = new Store(); store.Seed(MovementType.Credit, 1m); var original = store.Seed(MovementType.Debit, 1m);
        store.Seed(MovementType.Credit, Account.MaximumBalance);
        await Assert.ThrowsAsync<MovementConflictException>(() => new ReverseMovementHandler(store).ExecuteAsync(store.Command(original), default));
        Assert.Equal(Account.MaximumBalance, store.Account.Balance); Assert.Equal(0, store.Commits);
    }
    [Fact] public async Task OrdinaryOperationKeyCannotBeUsedForReversal()
    {
        var store = new Store(); var original = store.Seed(MovementType.Credit, 1m);
        await Assert.ThrowsAsync<MovementConflictException>(() => new ReverseMovementHandler(store).ExecuteAsync(store.Command(original) with { Key = original.IdempotencyKey }, default));
        Assert.Equal(1m, store.Account.Balance);
    }
    [Theory] [InlineData("")] [InlineData(" ")]
    public async Task InvalidKeyIsRejectedBeforeTransaction(string key)
    {
        var store = new Store(); var original = store.Seed(MovementType.Credit, 1m);
        await Assert.ThrowsAsync<ArgumentException>(() => new ReverseMovementHandler(store).ExecuteAsync(store.Command(original) with { Key = key }, default));
        Assert.False(store.Opened);
    }
}
