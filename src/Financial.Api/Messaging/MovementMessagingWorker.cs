using System.Text;
using System.Text.Json;
using Financial.Application.Messaging;
using Financial.Infrastructure.Messaging;
using Financial.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using RabbitMQ.Client;
namespace Financial.Api.Messaging;

// One bounded batch per iteration; multiple workers share outbox rows using SKIP LOCKED.
public sealed class MovementMessagingWorker(
    IDbContextFactory<FinancialDbContext> contexts, IConfiguration configuration,
    ReceiptStore receipts, ILogger<MovementMessagingWorker> logger) : BackgroundService
{
    public const string Exchange = "financial.events.v1";
    public const string Queue = "financial.receipts.v1";
    public const string DeadExchange = "financial.dead.v1";
    public const string DeadQueue = "financial.receipts.dead.v1";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var factory = new ConnectionFactory
                {
                    HostName = configuration["Messaging:Host"] ?? "rabbitmq",
                    UserName = configuration["Messaging:User"] ?? "financial_local",
                    Password = configuration["Messaging:Password"] ?? throw new InvalidOperationException("Messaging password required."),
                    RequestedConnectionTimeout = TimeSpan.FromSeconds(5),
                    AutomaticRecoveryEnabled = true
                };
                await using var connection = await factory.CreateConnectionAsync(stoppingToken);
                await using var channel = await connection.CreateChannelAsync(
                    new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true), stoppingToken);
                await ConfigureTopologyAsync(channel, stoppingToken);
                while (!stoppingToken.IsCancellationRequested)
                {
                    await PublishBatchAsync(channel, stoppingToken);
                    await ConsumeBatchAsync(channel, stoppingToken);
                    await Task.Delay(TimeSpan.FromMilliseconds(250), stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception)
            {
                // Do not log payloads, customer identifiers, connection strings or credentials.
                logger.LogWarning("Messaging cycle failed ({ErrorType}); pending work will be retried.", exception.GetType().Name);
                try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            }
        }
    }

    public static async Task ConfigureTopologyAsync(IChannel channel, CancellationToken ct)
    {
        await channel.ExchangeDeclareAsync(Exchange, ExchangeType.Fanout, durable: true, cancellationToken: ct);
        await channel.ExchangeDeclareAsync(DeadExchange, ExchangeType.Fanout, durable: true, cancellationToken: ct);
        await channel.QueueDeclareAsync(DeadQueue, durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object?> { ["x-queue-type"] = "quorum" }, cancellationToken: ct);
        await channel.QueueBindAsync(DeadQueue, DeadExchange, "", cancellationToken: ct);
        await channel.QueueDeclareAsync(Queue, durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object?> { ["x-queue-type"] = "quorum", ["x-delivery-limit"] = 5,
                ["x-dead-letter-exchange"] = DeadExchange, ["x-dead-letter-strategy"] = "at-least-once",
                ["x-overflow"] = "reject-publish" }, cancellationToken: ct);
        await channel.QueueBindAsync(Queue, Exchange, "", cancellationToken: ct);
    }

    private async Task PublishBatchAsync(IChannel channel, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var messages = await db.OutboxMessages.FromSqlRaw("""
            SELECT * FROM outbox_messages WHERE "PublishedAt" IS NULL
            ORDER BY "CreatedAt", "Id" LIMIT 20 FOR UPDATE SKIP LOCKED
            """).ToListAsync(ct);
        foreach (var message in messages)
        {
            // Confirm before marking published. Crash between these operations causes a safe redelivery.
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            await channel.BasicPublishAsync(Exchange, "", mandatory: true,
                basicProperties: new BasicProperties { Persistent = true, ContentType = "application/json", MessageId = message.Id.ToString(), Type = "MovementRecorded" },
                body: Encoding.UTF8.GetBytes(message.Payload), cancellationToken: timeout.Token);
            message.MarkPublished(DateTimeOffset.UtcNow);
        }
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    private async Task ConsumeBatchAsync(IChannel channel, CancellationToken ct)
    {
        // Bounded polling deliberately avoids an additional worker deployment for this small assessment.
        for (var i = 0; i < 20; i++)
        {
            var delivery = await channel.BasicGetAsync(Queue, autoAck: false, cancellationToken: ct);
            if (delivery is null) return;
            try
            {
                var message = MovementRecorded.Deserialize(delivery.Body.Span);
                await receipts.RecordAsync(message, ct);
                await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, cancellationToken: ct);
            }
            catch (Exception exception) when (exception is JsonException or ArgumentException)
            {
                logger.LogWarning("Rejected invalid integration event; routed to dead-letter queue.");
                await channel.BasicRejectAsync(delivery.DeliveryTag, requeue: false, cancellationToken: ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception)
            {
                // Quorum delivery limit bounds retries and routes repeatedly failing deliveries to the DLQ.
                await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: true, cancellationToken: ct);
                throw;
            }
        }
    }
}
