using Financial.Application.Provisioning;
using Financial.Domain.Accounts;
using Financial.Domain.Customers;
using Xunit;
namespace Financial.Domain.Tests;
public sealed class ProvisioningTests
{
    private sealed class Store : IProvisioningStore
    {
        public Customer? Customer { get; private set; }
        public Account? Account { get; private set; }
        public bool Exists { get; init; } = true;
        public Task AddCustomerAsync(Customer customer, CancellationToken ct) { Customer = customer; return Task.CompletedTask; }
        public Task<bool> CustomerExistsAsync(Guid id, CancellationToken ct) => Task.FromResult(Exists);
        public Task AddAccountAsync(Account account, CancellationToken ct) { Account = account; return Task.CompletedTask; }
        public Task<CustomerResult?> GetCustomerAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<AccountResult?> GetAccountAsync(Guid id, string subject, bool admin, CancellationToken ct) => throw new NotSupportedException();
    }
    [Fact] public async Task CustomerNameIsTrimmedAndIdentityIsPreserved()
    { var store = new Store(); var result = await new ProvisioningHandler(store, TimeProvider.System).CreateCustomerAsync("  Cliente  ", "subject", default); Assert.Equal("Cliente", result.Name); Assert.Equal("subject", result.SubjectId); Assert.NotNull(store.Customer); }
    [Theory] [InlineData("")] [InlineData(" ")] [InlineData(" leading")]
    public async Task InvalidIdentityDoesNotPersist(string subject)
    { var store = new Store(); await Assert.ThrowsAsync<ArgumentException>(() => new ProvisioningHandler(store, TimeProvider.System).CreateCustomerAsync("Name", subject, default)); Assert.Null(store.Customer); }
    [Fact] public async Task NewAccountStartsAtZeroInBrl()
    { var store = new Store(); var id = Guid.NewGuid(); var result = await new ProvisioningHandler(store, TimeProvider.System).OpenAccountAsync(id, default); Assert.Equal(id, result.CustomerId); Assert.Equal("BRL", result.Currency); Assert.Equal(0m, store.Account!.Balance); }
    [Fact] public async Task UnknownCustomerDoesNotCreateAccount()
    { var store = new Store { Exists = false }; await Assert.ThrowsAsync<CustomerNotFoundException>(() => new ProvisioningHandler(store, TimeProvider.System).OpenAccountAsync(Guid.NewGuid(), default)); Assert.Null(store.Account); }
}
