using Financial.Domain.Movements;
namespace Financial.Application.Messaging;

// Versioned integration contract; no customer name, token or idempotency key is published.
public sealed record MovementRecorded(int Version, Guid EventId, Guid MovementId, Guid AccountId,
    MovementType Type, decimal Amount, DateTimeOffset RegisteredAt)
{
    public static MovementRecorded From(Movement movement) => new(1, movement.Id, movement.Id,
        movement.AccountId, movement.Type, movement.Amount, movement.RegisteredAt);

    public void Validate()
    {
        if (Version != 1 || EventId == Guid.Empty || MovementId == Guid.Empty || AccountId == Guid.Empty
            || EventId != MovementId || !Enum.IsDefined(Type) || RegisteredAt == default)
            throw new ArgumentException("Unsupported or invalid movement event.");
        Financial.Domain.Accounts.Account.ValidateAmount(Amount);
    }
}
