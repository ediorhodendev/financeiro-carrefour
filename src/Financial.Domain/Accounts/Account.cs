namespace Financial.Domain.Accounts;

public sealed class Account
{
    public const decimal MaximumBalance = 9999999999999999.99m;
    private Account() { }
    public Account(Guid customerId, DateTimeOffset createdAt)
    {
        if (customerId == Guid.Empty) throw new ArgumentException("Customer is required.", nameof(customerId));
        Id = Guid.NewGuid(); CustomerId = customerId; CreatedAt = createdAt.ToUniversalTime();
    }
    public Guid Id { get; private set; }
    public Guid CustomerId { get; private set; }
    public string Currency { get; private set; } = "BRL";
    public decimal Balance { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public void Credit(decimal amount)
    {
        ValidateAmount(amount);
        if (amount > MaximumBalance - Balance) throw new InvalidOperationException("Balance limit exceeded.");
        Balance += amount;
    }
    public void Debit(decimal amount)
    {
        ValidateAmount(amount);
        if (amount > Balance) throw new InvalidOperationException("Insufficient funds.");
        Balance -= amount;
    }
    public static void ValidateAmount(decimal amount)
    {
        if (amount <= 0 || amount > MaximumBalance || decimal.Round(amount, 2) != amount)
            throw new ArgumentOutOfRangeException(nameof(amount), "Amount must be positive with at most two decimal places.");
    }
}
