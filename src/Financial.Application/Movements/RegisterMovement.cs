using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Financial.Domain.Accounts;
using Financial.Domain.Movements;
namespace Financial.Application.Movements;

public sealed record RegisterMovementCommand(Guid AccountId, MovementType Type, decimal Amount, string Key, string SubjectId);
public sealed record MovementResult(Guid Id, Guid AccountId, MovementType Type, decimal Amount, DateTimeOffset RegisteredAt, bool Replayed);
public sealed class AccountNotFoundException() : Exception("Account not found.");
public sealed class MovementConflictException(string message) : Exception(message);

public interface IAccountTransaction : IAsyncDisposable
{
    Account Account { get; }
    Task<Movement?> FindMovementAsync(string key, CancellationToken cancellationToken);
    Task<DateTimeOffset> GetRegistrationTimeAsync(CancellationToken cancellationToken);
    Task CommitAsync(Movement movement, CancellationToken cancellationToken);
}
public interface IAccountTransactionFactory
{
    Task<IAccountTransaction> BeginAsync(Guid accountId, string subjectId, CancellationToken cancellationToken);
}

public sealed class RegisterMovementHandler(IAccountTransactionFactory transactions)
{
    public async Task<MovementResult> ExecuteAsync(RegisterMovementCommand command, CancellationToken ct)
    {
        Account.ValidateAmount(command.Amount);
        if (command.AccountId == Guid.Empty || !Enum.IsDefined(command.Type)) throw new ArgumentException("Invalid account or movement type.");
        if (string.IsNullOrWhiteSpace(command.SubjectId)) throw new ArgumentException("Subject is required.");
        if (string.IsNullOrWhiteSpace(command.Key) || command.Key.Length > 100) throw new ArgumentException("Idempotency-Key must have 1 to 100 characters.");
        var hash = ComputeHash(command.Type, command.Amount);
        await using var transaction = await transactions.BeginAsync(command.AccountId, command.SubjectId, ct);
        var existing = await transaction.FindMovementAsync(command.Key, ct);
        if (existing is not null)
        {
            if (!string.Equals(hash, existing.RequestHash, StringComparison.Ordinal))
                throw new MovementConflictException("Idempotency key already used with different content.");
            return ToResult(existing, true);
        }
        // Account enforces the monetary invariant; the transaction protects concurrent writers.
        try
        {
            if (command.Type == MovementType.Credit) transaction.Account.Credit(command.Amount);
            else transaction.Account.Debit(command.Amount);
        }
        catch (InvalidOperationException exception)
        {
            throw new MovementConflictException(exception.Message);
        }
        var registeredAt = await transaction.GetRegistrationTimeAsync(ct);
        var movement = new Movement(command.AccountId, command.Type, command.Amount, registeredAt, command.Key, hash);
        await transaction.CommitAsync(movement, ct);
        return ToResult(movement, false);
    }
    public static string ComputeHash(MovementType type, decimal amount) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes($"v1|{type}|{amount.ToString("0.00", CultureInfo.InvariantCulture)}")));
    private static MovementResult ToResult(Movement movement, bool replayed) => new(
        movement.Id, movement.AccountId, movement.Type, movement.Amount, movement.RegisteredAt, replayed);
}
