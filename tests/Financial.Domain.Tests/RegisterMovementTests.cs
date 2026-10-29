using Financial.Application.Movements;
using Financial.Domain.Accounts;
using Financial.Domain.Movements;
using Xunit;
namespace Financial.Domain.Tests;
public sealed class RegisterMovementTests
{
    [Fact] public async Task CreditCommitsOneMovement()
    {
        var factory = new FakeFactory(); var handler = new RegisterMovementHandler(factory);
        var result = await handler.ExecuteAsync(factory.Command(MovementType.Credit, 100m), default);
        Assert.False(result.Replayed); Assert.Equal(100m, factory.Account.Balance); Assert.Equal(1, factory.Commits); Assert.True(factory.Disposed);
    }
    [Fact] public async Task ReplayReturnsOriginalWithoutChangingBalance()
    {
        var factory = new FakeFactory(); var handler = new RegisterMovementHandler(factory);
        var command = factory.Command(MovementType.Credit, 10m);
        var first = await handler.ExecuteAsync(command, default);
        var second = await handler.ExecuteAsync(command with { Amount = 10.00m }, default);
        Assert.True(second.Replayed); Assert.Equal(first.Id, second.Id); Assert.Equal(first.RegisteredAt, second.RegisteredAt);
        Assert.Equal(10m, factory.Account.Balance); Assert.Equal(1, factory.Commits);
    }
    [Fact] public async Task DifferentContentWithSameKeyIsRejected()
    {
        var factory = new FakeFactory(); var handler = new RegisterMovementHandler(factory);
        var command = factory.Command(MovementType.Credit, 10m);
        await handler.ExecuteAsync(command, default);
        await Assert.ThrowsAsync<MovementConflictException>(() => handler.ExecuteAsync(command with { Amount = 20m }, default));
        Assert.Equal(10m, factory.Account.Balance); Assert.Equal(1, factory.Commits);
    }
    [Fact] public async Task InsufficientFundsNeverCommits()
    {
        var factory = new FakeFactory(); var handler = new RegisterMovementHandler(factory);
        await Assert.ThrowsAsync<MovementConflictException>(() => handler.ExecuteAsync(factory.Command(MovementType.Debit, 1m), default));
        Assert.Equal(0, factory.Commits); Assert.Equal(0m, factory.Account.Balance); Assert.True(factory.Disposed);
    }
    [Fact] public async Task InvalidAmountIsRejectedBeforeOpeningTransaction()
    {
        var factory = new FakeFactory(); var handler = new RegisterMovementHandler(factory);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => handler.ExecuteAsync(factory.Command(MovementType.Credit, 0.001m), default));
        Assert.False(factory.Opened);
    }
    private sealed class FakeFactory : IAccountTransactionFactory, IAccountTransaction
    {
        public Account Account { get; } = new(Guid.NewGuid(), DateTimeOffset.UtcNow);
        public Movement? Existing { get; private set; }
        public int Commits { get; private set; }
        public bool Opened { get; private set; }
        public bool Disposed { get; private set; }
        public RegisterMovementCommand Command(MovementType type, decimal amount) => new(Account.Id, type, amount, "test-key", "subject");
        public Task<IAccountTransaction> BeginAsync(Guid id, string subject, CancellationToken ct) { Opened = true; return Task.FromResult<IAccountTransaction>(this); }
        public Task<Movement?> FindMovementAsync(string key, CancellationToken ct) => Task.FromResult(Existing);
        public Task<DateTimeOffset> GetRegistrationTimeAsync(CancellationToken ct) => Task.FromResult(DateTimeOffset.UtcNow);
        public Task CommitAsync(Movement movement, CancellationToken ct) { Existing = movement; Commits++; return Task.CompletedTask; }
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }
}
