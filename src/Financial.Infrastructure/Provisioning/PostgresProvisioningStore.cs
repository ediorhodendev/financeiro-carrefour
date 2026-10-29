using Financial.Application.Provisioning;
using Financial.Domain.Accounts;
using Financial.Domain.Customers;
using Financial.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
namespace Financial.Infrastructure.Provisioning;
public sealed class PostgresProvisioningStore(IDbContextFactory<FinancialDbContext> contexts) : IProvisioningStore
{
    public async Task AddCustomerAsync(Customer customer, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        db.Customers.Add(customer);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        { throw new ProvisioningConflictException("Identity already registered."); }
    }
    public async Task<bool> CustomerExistsAsync(Guid id, CancellationToken ct)
    { await using var db = await contexts.CreateDbContextAsync(ct); return await db.Customers.AnyAsync(c => c.Id == id, ct); }
    public async Task AddAccountAsync(Account account, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        db.Accounts.Add(account);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        { throw new ProvisioningConflictException("Customer already has an account."); }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation })
        { throw new CustomerNotFoundException(); }
    }
    public async Task<CustomerResult?> GetCustomerAsync(Guid id, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        return await db.Customers.AsNoTracking().Where(c => c.Id == id)
            .Select(c => new CustomerResult(c.Id, c.Name, c.SubjectId, c.CreatedAt)).SingleOrDefaultAsync(ct);
    }
    public async Task<AccountResult?> GetAccountAsync(Guid id, string subjectId, bool administrator, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        return await db.Accounts.AsNoTracking().Where(a => a.Id == id && (administrator ||
                db.Customers.Any(c => c.Id == a.CustomerId && c.SubjectId == subjectId)))
            .Select(a => new AccountResult(a.Id, a.CustomerId, a.Currency, a.CreatedAt)).SingleOrDefaultAsync(ct);
    }
}
