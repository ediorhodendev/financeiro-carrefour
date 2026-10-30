using Financial.Domain.Accounts;
namespace Financial.Domain.Movements;
// Stable public operation codes. Never renumber or reuse these values.
public enum MovementType { Credit = 1, Debit = 2 }
public sealed class Movement
{
    private Movement() { }
    public Movement(Guid accountId, MovementType type, decimal amount, DateTimeOffset registeredAt, string key, string requestHash)
    {
        Account.ValidateAmount(amount);
        if (accountId == Guid.Empty) throw new ArgumentException("Account is required.", nameof(accountId));
        if (!Enum.IsDefined(type)) throw new ArgumentOutOfRangeException(nameof(type));
        if (string.IsNullOrWhiteSpace(key) || key.Length > 100) throw new ArgumentException("Invalid idempotency key.", nameof(key));
        if (requestHash.Length != 64 || !requestHash.All(Uri.IsHexDigit)) throw new ArgumentException("Invalid hash.", nameof(requestHash));
        Id = Guid.NewGuid(); AccountId = accountId; Type = type; Amount = amount;
        RegisteredAt = registeredAt.ToUniversalTime(); IdempotencyKey = key; RequestHash = requestHash;
    }
    public Guid? ReversalOfMovementId { get; private set; }
    public string? ReversalReason { get; private set; }
    public static string ValidateReversalReason(string? reason)
    {
        var normalized = reason?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > 500)
            throw new ArgumentException("Reversal reason must contain 1 to 500 nonblank characters.", nameof(reason));
        return normalized;
    }
    public Movement CreateReversal(DateTimeOffset registeredAt, string key, string requestHash, string reason)
    {
        if (ReversalOfMovementId is not null)
            throw new InvalidOperationException("A reversal cannot itself be reversed.");
        return new Movement(AccountId, Type == MovementType.Credit ? MovementType.Debit : MovementType.Credit,
            Amount, registeredAt, key, requestHash)
        {
            ReversalOfMovementId = Id,
            ReversalReason = ValidateReversalReason(reason)
        };
    }
    public Guid Id { get; private set; }
    public Guid AccountId { get; private set; }
    public MovementType Type { get; private set; }
    public decimal Amount { get; private set; }
    public DateTimeOffset RegisteredAt { get; private set; }
    public string IdempotencyKey { get; private set; } = string.Empty;
    public string RequestHash { get; private set; } = string.Empty;
}
