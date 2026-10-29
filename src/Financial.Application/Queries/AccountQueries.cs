using System.Text.Json;
using Financial.Application.Balances;
using Financial.Application.Movements;
using Financial.Domain.Movements;
namespace Financial.Application.Queries;

public sealed record AccountBalanceSnapshot(Guid AccountId, string Currency, decimal Balance, DateTimeOffset CreatedAt);
public sealed record MovementItem(Guid Id, MovementType Type, decimal Amount, DateTimeOffset RegisteredAt);
public sealed record MovementPage(IReadOnlyList<MovementItem> Items, string? NextCursor);
public sealed record MovementCursor(int Version, Guid AccountId, DateTimeOffset At, Guid Id);
public interface IAccountQueryStore
{
    Task<AccountBalanceSnapshot?> ReadBalanceAsync(Guid accountId, string subjectId, DateTimeOffset? at, CancellationToken ct);
    // null means nonexistent or inaccessible account; empty list means an authorized account with no movements.
    Task<IReadOnlyList<MovementItem>?> ReadMovementsAsync(Guid accountId, string subjectId, MovementCursor? after, int take, CancellationToken ct);
}
public static class CursorCodec
{
    public static string Encode(MovementCursor cursor) => Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(cursor))
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');
    public static MovementCursor? Decode(string? value, Guid accountId)
    {
        if (value is null) return null;
        try
        {
            if (value.Length is 0 or > 1024) throw new FormatException();
            var base64 = value.Replace('-', '+').Replace('_', '/');
            base64 = base64.PadRight((base64.Length + 3) / 4 * 4, '=');
            var cursor = JsonSerializer.Deserialize<MovementCursor>(Convert.FromBase64String(base64));
            if (cursor is null || cursor.Version != 1 || cursor.AccountId != accountId || cursor.Id == Guid.Empty || cursor.At.Offset != TimeSpan.Zero)
                throw new FormatException();
            return cursor;
        }
        catch (Exception exception) when (exception is FormatException or JsonException)
        { throw new ArgumentException("Invalid cursor.", nameof(value)); }
    }
}
public sealed class AccountQueries(IAccountQueryStore store, TimeProvider clock) : IGetBalance
{
    public async Task<BalanceResult> ExecuteAsync(Guid accountId, string subjectId, DateTimeOffset? at, CancellationToken ct)
    {
        ValidateIdentity(accountId, subjectId);
        at = at?.ToUniversalTime();
        if (at > clock.GetUtcNow()) throw new ArgumentException("Historical instant cannot be in the future.", nameof(at));
        var snapshot = await store.ReadBalanceAsync(accountId, subjectId, at, ct) ?? throw new AccountNotFoundException();
        if (at < snapshot.CreatedAt) throw new ArgumentException("Historical instant predates the account.", nameof(at));
        return new BalanceResult(snapshot.AccountId, snapshot.Currency, snapshot.Balance, at);
    }
    public async Task<MovementPage> ListAsync(Guid accountId, string subjectId, int limit, string? cursor, CancellationToken ct)
    {
        ValidateIdentity(accountId, subjectId);
        if (limit is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(limit), "Limit must be between 1 and 100.");
        var after = CursorCodec.Decode(cursor, accountId);
        var rows = await store.ReadMovementsAsync(accountId, subjectId, after, limit + 1, ct) ?? throw new AccountNotFoundException();
        var items = rows.Take(limit).ToArray();
        var next = rows.Count > limit ? CursorCodec.Encode(new MovementCursor(1, accountId, items[^1].RegisteredAt, items[^1].Id)) : null;
        return new MovementPage(items, next);
    }
    private static void ValidateIdentity(Guid accountId, string subjectId)
    {
        if (accountId == Guid.Empty || string.IsNullOrWhiteSpace(subjectId)) throw new ArgumentException("Invalid identity.");
    }
}
