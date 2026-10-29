namespace Financial.Application.Balances;
public sealed record BalanceResult(Guid AccountId, string Currency, decimal Balance, DateTimeOffset? At);
public interface IGetBalance
{
    Task<BalanceResult> ExecuteAsync(Guid accountId, string subjectId, DateTimeOffset? at, CancellationToken cancellationToken);
}
