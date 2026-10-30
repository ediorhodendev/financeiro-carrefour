using System.Security.Cryptography;
using System.Text;
using Financial.Domain.Movements;
namespace Financial.Application.Movements;

public sealed record ReverseMovementCommand(Guid AccountId, Guid MovementId, string Reason, string Key, string SubjectId);
public sealed record ReversalResult(Guid Id, Guid AccountId, Guid ReversalOfMovementId, MovementType Type,
    decimal Amount, string Reason, DateTimeOffset RegisteredAt, bool Replayed);
public sealed class MovementNotFoundException() : Exception("Movement not found in this account.");

public sealed class ReverseMovementHandler(IAccountTransactionFactory transactions)
{
    public async Task<ReversalResult> ExecuteAsync(ReverseMovementCommand command, CancellationToken ct)
    {
        if (command.AccountId == Guid.Empty || command.MovementId == Guid.Empty || string.IsNullOrWhiteSpace(command.SubjectId))
            throw new ArgumentException("Valid account, movement and authenticated subject are required.");
        if (string.IsNullOrWhiteSpace(command.Key) || command.Key.Length > 100)
            throw new ArgumentException("Idempotency-Key must have 1 to 100 characters.");
        var reason = Movement.ValidateReversalReason(command.Reason);
        // Distinct operation namespace prevents collisions with ordinary credit/debit keys.
        var hash = ComputeHash(command.MovementId, reason);
        await using var transaction = await transactions.BeginAsync(command.AccountId, command.SubjectId, ct);
        var existing = await transaction.FindMovementAsync(command.Key, ct);
        if (existing is not null)
        {
            if (existing.RequestHash != hash || existing.ReversalOfMovementId != command.MovementId)
                throw new MovementConflictException("Idempotency key already used with different content.");
            return ToResult(existing, true);
        }
        var original = await transaction.FindMovementByIdAsync(command.MovementId, ct) ?? throw new MovementNotFoundException();
        if (original.ReversalOfMovementId is not null)
            throw new MovementConflictException("A reversal cannot itself be reversed.");
        if (await transaction.HasReversalAsync(original.Id, ct))
            throw new MovementConflictException("Movement has already been reversed.");
        var reversal = original.CreateReversal(await transaction.GetRegistrationTimeAsync(ct), command.Key, hash, reason);
        try
        {
            if (reversal.Type == MovementType.Credit) transaction.Account.Credit(reversal.Amount);
            else transaction.Account.Debit(reversal.Amount);
        }
        catch (InvalidOperationException exception) { throw new MovementConflictException(exception.Message); }
        await transaction.CommitAsync(reversal, ct);
        return ToResult(reversal, false);
    }
    public static string ComputeHash(Guid movementId, string normalizedReason) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes($"v1|reversal|{movementId:D}|{normalizedReason}")));
    private static ReversalResult ToResult(Movement movement, bool replayed) => new(movement.Id, movement.AccountId,
        movement.ReversalOfMovementId!.Value, movement.Type, movement.Amount, movement.ReversalReason!, movement.RegisteredAt, replayed);
}
