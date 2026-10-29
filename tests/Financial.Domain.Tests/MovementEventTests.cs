using Financial.Application.Messaging;
using Financial.Application.Movements;
using Financial.Domain.Movements;
using Xunit;
namespace Financial.Domain.Tests;

public sealed class MovementEventTests
{
    private static Movement Movement() => new(Guid.NewGuid(), MovementType.Credit, 12.34m,
        DateTimeOffset.UtcNow, "test-key", RegisterMovementHandler.ComputeHash(MovementType.Credit, 12.34m));
    [Fact] public void EventPreservesFinancialValuesAndUsesStableIdentifier()
    {
        var movement = Movement(); var message = MovementRecorded.From(movement);
        message.Validate();
        Assert.Equal(movement.Id, message.EventId); Assert.Equal(movement.Id, message.MovementId);
        Assert.Equal(movement.AccountId, message.AccountId); Assert.Equal(12.34m, message.Amount);
        Assert.Equal(movement.RegisteredAt, message.RegisteredAt); Assert.Equal(1, message.Version);
    }
    [Theory] [InlineData(0)] [InlineData(2)]
    public void UnsupportedVersionsAreRejected(int version) => Assert.Throws<ArgumentException>(() =>
        (MovementRecorded.From(Movement()) with { Version = version }).Validate());
    [Fact] public void EmptyEventIdIsRejected() => Assert.Throws<ArgumentException>(() =>
        (MovementRecorded.From(Movement()) with { EventId = Guid.Empty }).Validate());
    [Fact] public void MismatchedIdentifiersAreRejected() => Assert.Throws<ArgumentException>(() =>
        (MovementRecorded.From(Movement()) with { MovementId = Guid.NewGuid() }).Validate());
    [Fact] public void EmptyAccountIsRejected() => Assert.Throws<ArgumentException>(() =>
        (MovementRecorded.From(Movement()) with { AccountId = Guid.Empty }).Validate());
    [Fact] public void MissingTimestampIsRejected() => Assert.Throws<ArgumentException>(() =>
        (MovementRecorded.From(Movement()) with { RegisteredAt = default }).Validate());
    [Fact] public void UnknownTypeIsRejected() => Assert.Throws<ArgumentException>(() =>
        (MovementRecorded.From(Movement()) with { Type = (MovementType)99 }).Validate());
    [Theory] [InlineData(0)] [InlineData(-1)] [InlineData(0.001)]
    public void InvalidEventAmountIsRejected(decimal amount) => Assert.Throws<ArgumentOutOfRangeException>(() =>
        (MovementRecorded.From(Movement()) with { Amount = amount }).Validate());
}
