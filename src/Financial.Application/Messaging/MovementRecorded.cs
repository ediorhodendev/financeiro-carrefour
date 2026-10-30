using Financial.Domain.Movements;
using System.Text.Json;
namespace Financial.Application.Messaging;

// Versioned integration contract; no customer name, token or idempotency key is published.
public sealed record MovementRecorded(int Version, Guid EventId, Guid MovementId, Guid AccountId,
    MovementType Type, decimal Amount, DateTimeOffset RegisteredAt, Guid? ReversalOfMovementId = null)
{
    public const int CurrentVersion = 2;
    public static MovementRecorded From(Movement movement) => new(CurrentVersion, movement.Id, movement.Id,
        movement.AccountId, movement.Type, movement.Amount, movement.RegisteredAt, movement.ReversalOfMovementId);

    public static MovementRecorded Deserialize(ReadOnlySpan<byte> payload)
    {
        var message = JsonSerializer.Deserialize<MovementRecorded>(payload)
            ?? throw new JsonException("Empty integration event.");
        // Old v1 events encoded Credit=0/Debit=1. Normalize before validating the v2 contract.
        // This is required for events already persisted in Outbox or waiting in RabbitMQ.
        if (message.Version == 1)
        {
            var normalizedType = (int)message.Type switch
            {
                0 => MovementType.Credit,
                1 => MovementType.Debit,
                _ => throw new ArgumentException("Invalid legacy movement code.")
            };
            message = message with { Version = CurrentVersion, Type = normalizedType };
        }
        message.Validate();
        return message;
    }
    public void Validate()
    {
        if (Version != CurrentVersion || EventId == Guid.Empty || MovementId == Guid.Empty || AccountId == Guid.Empty
            || ReversalOfMovementId == Guid.Empty || ReversalOfMovementId == MovementId
            || EventId != MovementId || !Enum.IsDefined(Type) || RegisteredAt == default)
            throw new ArgumentException("Unsupported or invalid movement event.");
        Financial.Domain.Accounts.Account.ValidateAmount(Amount);
    }
}
