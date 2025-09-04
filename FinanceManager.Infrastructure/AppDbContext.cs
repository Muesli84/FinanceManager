using FinanceManager.Domain.Users;
using FinanceManager.Domain.Accounts;
using FinanceManager.Domain.Contacts;
using FinanceManager.Domain.Statements;
using FinanceManager.Domain.Postings;
using Microsoft.EntityFrameworkCore;
using FinanceManager.Domain.Savings;
using Microsoft.EntityFrameworkCore.Diagnostics;
using FinanceManager.Shared.Dtos;

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
    public DbSet<SavingsPlanCategory> SavingsPlanCategories { get; set; } = null!;


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
            b.Property(x => x.SavingsPlanId);
            b.HasOne<SavingsPlan>()
             .WithMany()
             .HasForeignKey(x => x.SavingsPlanId)
             .OnDelete(DeleteBehavior.SetNull);
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

        // SavingsPlanCategory
        modelBuilder.Entity<SavingsPlanCategory>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
            entity.Property(e => e.OwnerUserId).IsRequired();
        });

        // SavingsPlan
        modelBuilder.Entity<SavingsPlan>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(x => new { x.OwnerUserId, x.Name }).IsUnique();
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
            entity.Property(x => x.TargetAmount).HasPrecision(18, 2);
            entity.Property(x => x.CreatedUtc).IsRequired();
            entity.Property(x => x.IsActive).IsRequired();
            entity.Property(e => e.OwnerUserId).IsRequired();
            entity.HasOne<SavingsPlanCategory>()
                  .WithMany()
                  .HasForeignKey(e => e.CategoryId)
                  .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<StatementDraftEntry>(b =>
        {
            b.Property<Guid?>("SplitDraftId")
                .HasColumnType("uniqueidentifier");

            b.HasIndex("SplitDraftId")
                .IsUnique()
                .HasFilter("[SplitDraftId] IS NOT NULL");

            b.HasOne<StatementDraft>()
                .WithMany()
                .HasForeignKey("SplitDraftId")
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.ConfigureWarnings(w => w.Ignore(RelationalEventId.NonTransactionalMigrationOperationWarning));
    }

    internal void ClearUserData(Guid userId)
    {
        Postings.RemoveRange(Postings.Where(p => Accounts.Any(a => a.Id == p.AccountId && a.OwnerUserId == userId)));
        StatementEntries.RemoveRange(StatementEntries.Where(e => StatementImports.Any(i => Accounts.Any(a => a.Id == i.AccountId && a.OwnerUserId == userId) && e.StatementImportId == e.StatementImportId)));
        StatementImports.RemoveRange(StatementImports.Where(i => Accounts.Any(a => a.Id == i.AccountId && a.OwnerUserId == userId)));
        StatementDraftEntries.RemoveRange(StatementDraftEntries.Where(e => StatementDrafts.Any(d => d.Id == e.DraftId && d.OwnerUserId == userId)));
        StatementDrafts.RemoveRange(StatementDrafts.Where(d => d.OwnerUserId == userId));
        SavingsPlans.RemoveRange(SavingsPlans.Where(s => s.OwnerUserId == userId));
        SavingsPlanCategories.RemoveRange(SavingsPlanCategories.Where(c => c.OwnerUserId == userId));
        Contacts.RemoveRange(Contacts.Where(c => c.OwnerUserId == userId && c.Type != ContactType.Self));
        ContactCategories.RemoveRange(ContactCategories.Where(c => c.OwnerUserId == userId));
        AliasNames.RemoveRange(AliasNames.Where(a => Contacts.Any(c => c.Id == a.ContactId && c.OwnerUserId == userId)));
        AccountShares.RemoveRange(AccountShares.Where(s => s.UserId == userId || Accounts.Any(a => a.OwnerUserId == userId && a.Id == s.AccountId)));
        Accounts.RemoveRange(Accounts.Where(a => a.OwnerUserId == userId));
    }
}
