using Financial.Application.Movements;
using Financial.Application.Queries;
using Financial.Domain.Movements;
using Xunit;
namespace Financial.Domain.Tests;
public sealed class AccountQueriesTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class Store : IAccountQueryStore
    {
        public Guid Id { get; } = Guid.NewGuid();
        public DateTimeOffset? ReceivedAt { get; private set; }
        public bool Read { get; private set; }
        public bool Found { get; init; } = true;
        public List<MovementItem> Items { get; } = [];
        public Task<AccountBalanceSnapshot?> ReadBalanceAsync(Guid id, string subject, DateTimeOffset? at, CancellationToken ct)
        {
            Read = true; ReceivedAt = at;
            return Task.FromResult<AccountBalanceSnapshot?>(Found ? new(id, "BRL", 70m, Now.AddDays(-1)) : null);
        }
        public Task<IReadOnlyList<MovementItem>?> ReadMovementsAsync(Guid id, string subject, MovementCursor? after, int take, CancellationToken ct)
        { Read = true; return Task.FromResult<IReadOnlyList<MovementItem>?>(Found ? Items.Take(take).ToArray() : null); }
    }
    [Fact] public async Task CurrentBalanceUsesCurrentProjection()
    { var store = new Store(); var result = await new AccountQueries(store, new Clock()).ExecuteAsync(store.Id, "owner", null, default); Assert.Equal(70m, result.Balance); Assert.Null(result.At); Assert.Null(store.ReceivedAt); }
    [Fact] public async Task HistoricalOffsetIsNormalizedToUtc()
    { var store = new Store(); var at = Now.AddHours(-1).ToOffset(TimeSpan.FromHours(-3)); var result = await new AccountQueries(store, new Clock()).ExecuteAsync(store.Id, "owner", at, default); Assert.Equal(TimeSpan.Zero, result.At!.Value.Offset); Assert.Equal(at, store.ReceivedAt); }
    [Fact] public async Task FutureInstantIsRejectedBeforeReading()
    { var store = new Store(); await Assert.ThrowsAsync<ArgumentException>(() => new AccountQueries(store, new Clock()).ExecuteAsync(store.Id, "owner", Now.AddSeconds(1), default)); Assert.False(store.Read); }
    [Fact] public async Task InstantBeforeAccountCreationIsRejected()
    { var store = new Store(); await Assert.ThrowsAsync<ArgumentException>(() => new AccountQueries(store, new Clock()).ExecuteAsync(store.Id, "owner", Now.AddDays(-2), default)); }
    [Fact] public async Task InaccessibleAccountIsNotFound()
    { var store = new Store { Found = false }; await Assert.ThrowsAsync<AccountNotFoundException>(() => new AccountQueries(store, new Clock()).ExecuteAsync(store.Id, "owner", null, default)); }
    [Theory] [InlineData(0)] [InlineData(101)]
    public async Task LimitIsValidatedBeforeReading(int limit)
    { var store = new Store(); await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => new AccountQueries(store, new Clock()).ListAsync(store.Id, "owner", limit, null, default)); Assert.False(store.Read); }
    [Fact] public async Task PageUsesLookaheadAndCursorOfLastReturnedItem()
    {
        var store = new Store(); store.Items.Add(new(Guid.NewGuid(), MovementType.Credit, 10m, Now.AddMinutes(-2))); store.Items.Add(new(Guid.NewGuid(), MovementType.Debit, 2m, Now.AddMinutes(-1)));
        var result = await new AccountQueries(store, new Clock()).ListAsync(store.Id, "owner", 1, null, default);
        var item = Assert.Single(result.Items); Assert.NotNull(result.NextCursor);
        Assert.Equal(item.Id, CursorCodec.Decode(result.NextCursor, store.Id)!.Id);
    }
    [Fact] public async Task EmptyPageHasNoCursor()
    { var store = new Store(); var result = await new AccountQueries(store, new Clock()).ListAsync(store.Id, "owner", 50, null, default); Assert.Empty(result.Items); Assert.Null(result.NextCursor); }
    [Fact] public void CursorForDifferentAccountIsRejected()
    { var cursor = CursorCodec.Encode(new(1, Guid.NewGuid(), Now, Guid.NewGuid())); Assert.Throws<ArgumentException>(() => CursorCodec.Decode(cursor, Guid.NewGuid())); }
    [Theory] [InlineData("")] [InlineData("garbage")] [InlineData("bnVsbA")]
    public void MalformedCursorIsRejected(string cursor) => Assert.Throws<ArgumentException>(() => CursorCodec.Decode(cursor, Guid.NewGuid()));
}
