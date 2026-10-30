using System.Text.Json;
using Financial.Application.Messaging;
using Financial.Application.Movements;
using Financial.Domain.Movements;
using Xunit;
namespace Financial.Domain.Tests;
public sealed class MovementCodeTests
{
    [Fact] public void OperationCodesAreStableAndZeroIsUndefined()
    {
        Assert.Equal(1, (int)MovementType.Credit); Assert.Equal(2, (int)MovementType.Debit);
        Assert.False(Enum.IsDefined((MovementType)0));
    }
    [Theory] [InlineData(1, MovementType.Credit)] [InlineData(2, MovementType.Debit)]
    public void Version2EventsRoundTripNumericCodes(int code, MovementType type)
    {
        var movement = new Movement(Guid.NewGuid(), type, 1m, DateTimeOffset.UtcNow, "key", RegisterMovementHandler.ComputeHash(type, 1m));
        var payload = JsonSerializer.SerializeToUtf8Bytes(MovementRecorded.From(movement));
        using var json = JsonDocument.Parse(payload);
        Assert.Equal(2, json.RootElement.GetProperty("Version").GetInt32());
        Assert.Equal(code, json.RootElement.GetProperty("Type").GetInt32());
        Assert.Equal(type, MovementRecorded.Deserialize(payload).Type);
    }
    [Theory] [InlineData(0, MovementType.Credit)] [InlineData(1, MovementType.Debit)]
    public void Version1EventsAreTranslatedWithoutReinterpretingDebitAsCredit(int oldCode, MovementType expected)
    {
        var id = Guid.NewGuid();
        var payload = JsonSerializer.SerializeToUtf8Bytes(new { Version = 1, EventId = id, MovementId = id,
            AccountId = Guid.NewGuid(), Type = oldCode, Amount = 5m, RegisteredAt = DateTimeOffset.UtcNow });
        var result = MovementRecorded.Deserialize(payload);
        Assert.Equal(2, result.Version); Assert.Equal(expected, result.Type); Assert.Equal(id, result.EventId);
    }
    [Theory] [InlineData(1, 2)] [InlineData(2, 0)] [InlineData(2, 3)] [InlineData(3, 1)]
    public void UnsupportedWireCodesAndVersionsAreRejected(int version, int type)
    {
        var id = Guid.NewGuid();
        var payload = JsonSerializer.SerializeToUtf8Bytes(new { Version = version, EventId = id, MovementId = id,
            AccountId = Guid.NewGuid(), Type = type, Amount = 5m, RegisteredAt = DateTimeOffset.UtcNow });
        Assert.Throws<ArgumentException>(() => MovementRecorded.Deserialize(payload));
    }
    [Fact] public void ExistingIdempotencyHashesRemainStableBecauseTheyUseNames()
    {
        var expected = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("v1|Credit|100.00")));
        Assert.Equal(expected, RegisterMovementHandler.ComputeHash(MovementType.Credit, 100m));
    }
}
