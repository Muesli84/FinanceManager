using FinanceManager.Domain.Users;
using FinanceManager.Domain.Accounts;
using FinanceManager.Domain.Contacts;
using FinanceManager.Domain.Statements;
using FinanceManager.Domain.Postings;
using Microsoft.EntityFrameworkCore;
using FinanceManager.Domain.Savings;

namespace FinanceManager.Infrastructure;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<AccountShare> AccountShares => Set<AccountShare>();
    public DbSet<Contact> Contacts => Set<Contact>();
    public DbSet<ContactCategory> ContactCategories => Set<ContactCategory>();
    public DbSet<AliasName> AliasNames => Set<AliasName>();
    public DbSet<StatementImport> StatementImports => Set<StatementImport>();
    public DbSet<StatementEntry> StatementEntries => Set<StatementEntry>();
    public DbSet<Posting> Postings => Set<Posting>();
    public DbSet<StatementDraft> StatementDrafts => Set<StatementDraft>();
    public DbSet<StatementDraftEntry> StatementDraftEntries => Set<StatementDraftEntry>();
    public DbSet<SavingsPlan> SavingsPlans => Set<SavingsPlan>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<User>(b =>
        {
            b.HasIndex(x => x.Username).IsUnique();
            b.Property(x => x.Username).HasMaxLength(100).IsRequired();
            b.Property(x => x.PasswordHash).IsRequired();
        });

        modelBuilder.Entity<Account>(b =>
        {
            b.HasIndex(x => new { x.OwnerUserId, x.Name }).IsUnique();
            b.Property(x => x.Name).HasMaxLength(150).IsRequired();
            b.Property(x => x.Iban).HasMaxLength(34);
        });

        modelBuilder.Entity<Contact>(b =>
        {
            b.HasIndex(x => new { x.OwnerUserId, x.Name });
            b.Property(x => x.Name).HasMaxLength(200).IsRequired();
            b.HasOne<ContactCategory>()
             .WithMany()
             .HasForeignKey(x => x.CategoryId)
             .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<ContactCategory>(b =>
        {
            b.HasIndex(x => new { x.OwnerUserId, x.Name }).IsUnique();
            b.Property(x => x.Name).HasMaxLength(150).IsRequired();
        });

        modelBuilder.Entity<AliasName>(b =>
        {
            b.HasIndex(x => new { x.ContactId, x.Pattern }).IsUnique();
            b.Property(x => x.Pattern).HasMaxLength(200).IsRequired();
        });

        modelBuilder.Entity<StatementImport>(b =>
        {
            b.HasIndex(x => new { x.AccountId, x.ImportedAtUtc });
            b.Property(x => x.OriginalFileName).HasMaxLength(255).IsRequired();
        });

        modelBuilder.Entity<StatementEntry>(b =>
        {
            b.HasIndex(x => x.RawHash).IsUnique();
            b.Property(x => x.Subject).HasMaxLength(500).IsRequired();
            b.Property(x => x.RawHash).HasMaxLength(128).IsRequired();
        });

        modelBuilder.Entity<Posting>(b =>
        {
            b.HasIndex(x => new { x.AccountId, x.BookingDate });
        });

        modelBuilder.Entity<StatementDraft>(b =>
        {
            b.Property(x => x.OriginalFileName).HasMaxLength(255).IsRequired();
            b.HasMany<StatementDraftEntry>("Entries")
              .WithOne()
              .HasForeignKey(e => e.DraftId)
              .OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.OwnerUserId, x.CreatedUtc });
        });

        modelBuilder.Entity<StatementDraftEntry>(b =>
        {
            b.Property(x => x.Subject).HasMaxLength(500).IsRequired();
            b.HasIndex(x => new { x.DraftId, x.BookingDate });
        });

        modelBuilder.Entity<SavingsPlan>(b =>
        {
            b.HasIndex(x => new { x.OwnerUserId, x.Name }).IsUnique();
            b.Property(x => x.Name).HasMaxLength(150).IsRequired();
            b.Property(x => x.TargetAmount).HasPrecision(18, 2);
            b.Property(x => x.CreatedUtc).IsRequired();
            b.Property(x => x.IsActive).IsRequired();
        });
    }
}
