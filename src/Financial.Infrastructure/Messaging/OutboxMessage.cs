using System.Text.Json;
using Financial.Application.Messaging;
using Financial.Domain.Movements;
namespace Financial.Infrastructure.Messaging;

public sealed class OutboxMessage
{
    private OutboxMessage() { }
    public OutboxMessage(Movement movement)
    {
        Id = movement.Id;
        Payload = JsonSerializer.Serialize(MovementRecorded.From(movement));
        CreatedAt = movement.RegisteredAt;
    }
    public Guid Id { get; private set; }
    public string Payload { get; private set; } = "";
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? PublishedAt { get; private set; }
    public void MarkPublished(DateTimeOffset now) => PublishedAt = now.ToUniversalTime();
}

// This table is both the inbox deduplication key and the consumer's observable effect.
public sealed class ProcessingReceipt
{
    public Guid EventId { get; set; }
    public Guid MovementId { get; set; }
    public Guid AccountId { get; set; }
    public DateTimeOffset ProcessedAt { get; set; }
}
