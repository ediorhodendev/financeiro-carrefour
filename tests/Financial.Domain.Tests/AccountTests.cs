using Financial.Domain.Accounts;
using Xunit;
namespace Financial.Domain.Tests;
public sealed class AccountTests
{
    private static Account Create() => new(Guid.NewGuid(), DateTimeOffset.UtcNow);
    [Fact] public void CreditAndDebitProduceExpectedBalance()
    { var account = Create(); account.Credit(100m); account.Debit(30m); Assert.Equal(70m, account.Balance); }
    [Fact] public void InsufficientFundsDoesNotChangeBalance()
    { var account = Create(); account.Credit(10m); Assert.Throws<InvalidOperationException>(() => account.Debit(11m)); Assert.Equal(10m, account.Balance); }
    [Theory] [InlineData(0)] [InlineData(-1)] [InlineData(0.001)]
    public void InvalidAmountDoesNotChangeBalance(double value)
    { var account = Create(); Assert.Throws<ArgumentOutOfRangeException>(() => account.Credit((decimal)value)); Assert.Equal(0m, account.Balance); }
    [Fact] public void BalanceLimitIsEnforcedWithoutMutation()
    { var account = Create(); account.Credit(Account.MaximumBalance); Assert.Throws<InvalidOperationException>(() => account.Credit(0.01m)); Assert.Equal(Account.MaximumBalance, account.Balance); }
    [Fact] public void ExactBalanceCanBeDebited()
    { var account = Create(); account.Credit(10m); account.Debit(10m); Assert.Equal(0m, account.Balance); }
}
