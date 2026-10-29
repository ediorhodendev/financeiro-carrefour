using Financial.Domain.Accounts;
using Financial.Domain.Customers;
namespace Financial.Application.Provisioning;

public sealed record CustomerResult(Guid Id, string Name, string SubjectId, DateTimeOffset CreatedAt);
public sealed record AccountResult(Guid Id, Guid CustomerId, string Currency, DateTimeOffset CreatedAt);
public sealed class ProvisioningConflictException(string message) : Exception(message);
public sealed class CustomerNotFoundException() : Exception("Customer not found.");
public interface IProvisioningStore
{
    Task AddCustomerAsync(Customer customer, CancellationToken ct);
    Task<bool> CustomerExistsAsync(Guid customerId, CancellationToken ct);
    Task AddAccountAsync(Account account, CancellationToken ct);
    Task<CustomerResult?> GetCustomerAsync(Guid id, CancellationToken ct);
    Task<AccountResult?> GetAccountAsync(Guid id, string subjectId, bool administrator, CancellationToken ct);
}
public sealed class ProvisioningHandler(IProvisioningStore store, TimeProvider clock)
{
    public async Task<CustomerResult> CreateCustomerAsync(string name, string subjectId, CancellationToken ct)
    {
        var customer = new Customer(name, subjectId, clock.GetUtcNow());
        await store.AddCustomerAsync(customer, ct);
        return new CustomerResult(customer.Id, customer.Name, customer.SubjectId, customer.CreatedAt);
    }
    public async Task<AccountResult> OpenAccountAsync(Guid customerId, CancellationToken ct)
    {
        if (customerId == Guid.Empty) throw new ArgumentException("Customer is required.", nameof(customerId));
        if (!await store.CustomerExistsAsync(customerId, ct)) throw new CustomerNotFoundException();
        var account = new Account(customerId, clock.GetUtcNow());
        await store.AddAccountAsync(account, ct);
        return new AccountResult(account.Id, account.CustomerId, account.Currency, account.CreatedAt);
    }
}
