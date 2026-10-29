using Financial.Infrastructure.Messaging;
using Financial.Domain.Accounts;
using Financial.Domain.Customers;
using Financial.Domain.Movements;
using Microsoft.EntityFrameworkCore;
namespace Financial.Infrastructure.Persistence;
public sealed class FinancialDbContext(DbContextOptions<FinancialDbContext> options) : DbContext(options)
{
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<ProcessingReceipt> ProcessingReceipts => Set<ProcessingReceipt>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Movement> Movements => Set<Movement>();
    private void ValidateLedgerChanges()
    {
        ChangeTracker.DetectChanges();
        if (ChangeTracker.Entries<Movement>().Any(e => e.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Confirmed movements are immutable; use a compensating operation.");
    }
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    { ValidateLedgerChanges(); return base.SaveChanges(acceptAllChangesOnSuccess); }
    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    { ValidateLedgerChanges(); return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken); }
    protected override void OnModelCreating(ModelBuilder model)
    {
        var outbox = model.Entity<OutboxMessage>();
        outbox.ToTable("outbox_messages"); outbox.HasKey(x => x.Id);
        outbox.Property(x => x.Payload).HasColumnType("jsonb").IsRequired();
        outbox.HasIndex(x => new { x.CreatedAt, x.Id }).HasFilter("\"PublishedAt\" IS NULL");
        var receipt = model.Entity<ProcessingReceipt>();
        receipt.ToTable("processing_receipts"); receipt.HasKey(x => x.EventId);
        receipt.HasIndex(x => x.AccountId);
        var customer = model.Entity<Customer>();
        customer.ToTable("customers"); customer.HasKey(x => x.Id);
        customer.Property(x => x.Name).HasMaxLength(150).IsRequired();
        customer.Property(x => x.SubjectId).HasMaxLength(200).IsRequired();
        customer.HasIndex(x => x.SubjectId).IsUnique();
        var account = model.Entity<Account>();
        account.ToTable("accounts", t => { t.HasCheckConstraint("ck_balance", "\"Balance\" >= 0"); t.HasCheckConstraint("ck_currency", "\"Currency\" = 'BRL'"); });
        account.HasKey(x => x.Id); account.HasIndex(x => x.CustomerId).IsUnique();
        account.Property(x => x.Balance).HasPrecision(18, 2);
        account.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        account.HasOne<Customer>().WithOne().HasForeignKey<Account>(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
        var movement = model.Entity<Movement>();
        movement.ToTable("movements", t => { t.HasCheckConstraint("ck_amount", "\"Amount\" > 0"); t.HasCheckConstraint("ck_type", "\"Type\" IN ('Credit', 'Debit')"); });
        movement.HasKey(x => x.Id); movement.Property(x => x.Amount).HasPrecision(18, 2);
        movement.Property(x => x.Type).HasConversion<string>().HasMaxLength(6);
        movement.Property(x => x.IdempotencyKey).HasMaxLength(100).IsRequired();
        movement.Property(x => x.RequestHash).HasMaxLength(64).IsRequired();
        movement.HasIndex(x => new { x.AccountId, x.IdempotencyKey }).IsUnique();
        movement.HasIndex(x => new { x.AccountId, x.RegisteredAt, x.Id });
        movement.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
    }
}
