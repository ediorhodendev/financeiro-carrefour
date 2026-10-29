using Financial.Domain.Accounts;
namespace Financial.Domain.Movements;
public enum MovementType { Credit, Debit }
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
    public Guid Id { get; private set; }
    public Guid AccountId { get; private set; }
    public MovementType Type { get; private set; }
    public decimal Amount { get; private set; }
    public DateTimeOffset RegisteredAt { get; private set; }
    public string IdempotencyKey { get; private set; } = string.Empty;
    public string RequestHash { get; private set; } = string.Empty;
}
