using FinanceManager.Domain.Users;
using FinanceManager.Domain.Accounts;
using FinanceManager.Domain.Contacts;
using FinanceManager.Domain.Statements;
using FinanceManager.Domain.Postings;
using Microsoft.EntityFrameworkCore;
using FinanceManager.Domain.Savings;
using Microsoft.EntityFrameworkCore.Diagnostics;
using FinanceManager.Shared.Dtos;
using FinanceManager.Domain.Securities;
using FinanceManager.Infrastructure.Backups;
using System.Threading.Tasks;
using System.Threading;
using FinanceManager.Domain.Reports; // added

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
    public DbSet<Security> Securities => Set<Security>();
    public DbSet<SecurityCategory> SecurityCategories => Set<SecurityCategory>();
    public DbSet<PostingAggregate> PostingAggregates => Set<PostingAggregate>();
    public DbSet<SecurityPrice> SecurityPrices => Set<SecurityPrice>();
    public DbSet<BackupRecord> Backups => Set<BackupRecord>();
    public DbSet<ReportFavorite> ReportFavorites => Set<ReportFavorite>(); // new
    public DbSet<HomeKpi> HomeKpis => Set<HomeKpi>(); // new


    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<User>(b =>
        {
            b.HasIndex(x => x.Username).IsUnique();
            b.Property(x => x.Username).HasMaxLength(100).IsRequired();
            b.Property(x => x.PasswordHash).IsRequired();
            // Import split settings columns
            b.Property(x => x.ImportSplitMode).HasConversion<short>().IsRequired();
            b.Property(x => x.ImportMaxEntriesPerDraft).IsRequired();
            b.Property(x => x.ImportMonthlySplitThreshold);
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
            // NEW: index for upload group
            b.HasIndex(x => x.UploadGroupId);
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
            entity.Property(e => e.ContractNumber).HasMaxLength(100);
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

        modelBuilder.Entity<Security>(b =>
        {
            b.HasKey(x => x.Id);
            b.HasIndex(x => new { x.OwnerUserId, x.Name }).IsUnique();
            b.HasIndex(x => new { x.OwnerUserId, x.Identifier });
            b.Property(x => x.Name).HasMaxLength(200).IsRequired();
            b.Property(x => x.Identifier).HasMaxLength(50).IsRequired();
            b.Property(x => x.CurrencyCode).HasMaxLength(10).IsRequired();
            b.Property(x => x.AlphaVantageCode).HasMaxLength(50);
            b.HasOne<SecurityCategory>()
                .WithMany()
                .HasForeignKey(x => x.CategoryId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<SecurityCategory>(b =>
        {
            b.HasKey(x => x.Id);
            b.HasIndex(x => new { x.OwnerUserId, x.Name }).IsUnique();
            b.Property(x => x.Name).HasMaxLength(150).IsRequired();
            b.Property(x => x.OwnerUserId).IsRequired();
        });

        modelBuilder.Entity<PostingAggregate>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Amount).HasPrecision(18,2);
            // broad unique index (may be NULL-sensitive depending on provider)
            b.HasIndex(x => new { x.Kind, x.AccountId, x.ContactId, x.SavingsPlanId, x.SecurityId, x.Period, x.PeriodStart }).IsUnique();
            // refined unique indexes per dimension combination (filters to non-null key parts)
            b.HasIndex(x => new { x.Kind, x.AccountId, x.Period, x.PeriodStart })
                .IsUnique()
                .HasFilter("[AccountId] IS NOT NULL AND [ContactId] IS NULL AND [SavingsPlanId] IS NULL AND [SecurityId] IS NULL");
            b.HasIndex(x => new { x.Kind, x.ContactId, x.Period, x.PeriodStart })
                .IsUnique()
                .HasFilter("[ContactId] IS NOT NULL AND [AccountId] IS NULL AND [SavingsPlanId] IS NULL AND [SecurityId] IS NULL");
            b.HasIndex(x => new { x.Kind, x.SavingsPlanId, x.Period, x.PeriodStart })
                .IsUnique()
                .HasFilter("[SavingsPlanId] IS NOT NULL AND [AccountId] IS NULL AND [ContactId] IS NULL AND [SecurityId] IS NULL");
            b.HasIndex(x => new { x.Kind, x.SecurityId, x.Period, x.PeriodStart })
                .IsUnique()
                .HasFilter("[SecurityId] IS NOT NULL AND [AccountId] IS NULL AND [ContactId] IS NULL AND [SavingsPlanId] IS NULL");
        });

        modelBuilder.Entity<StatementDraftEntry>(b =>
        {
            b.Property<Guid?>("SecurityId");
            b.Property<SecurityTransactionType?>("SecurityTransactionType");
            b.Property<decimal?>("SecurityQuantity").HasPrecision(18,6);
            b.Property<decimal?>("SecurityFeeAmount").HasPrecision(18,2);
            b.Property<decimal?>("SecurityTaxAmount").HasPrecision(18,2);
            b.HasIndex("SecurityId");
        });

        modelBuilder.Entity<SecurityPrice>(b =>
        {
            b.HasKey(x => x.Id);
            b.HasIndex(x => new { x.SecurityId, x.Date }).IsUnique();
            b.Property(x => x.Date).IsRequired();
            b.Property(x => x.Close).HasPrecision(18,4).IsRequired();
        });

        modelBuilder.Entity<BackupRecord>(b =>
        {
            b.HasKey(x => x.Id);
            b.HasIndex(x => new { x.OwnerUserId, x.CreatedUtc });
            b.Property(x => x.FileName).HasMaxLength(255).IsRequired();
            b.Property(x => x.Source).HasMaxLength(40).IsRequired();
            b.Property(x => x.StoragePath).HasMaxLength(400).IsRequired();
        });

        // ReportFavorite configuration
        modelBuilder.Entity<ReportFavorite>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Name).HasMaxLength(120).IsRequired();
            b.HasIndex(x => new { x.OwnerUserId, x.Name }).IsUnique();
            b.Property(x => x.PostingKind).IsRequired();
            b.Property(x => x.Interval).HasConversion<int>().IsRequired();
            b.Property(x => x.Take).IsRequired();
        });

        // HomeKpi configuration
        modelBuilder.Entity<HomeKpi>(b =>
        {
            b.HasKey(x => x.Id);
            b.HasIndex(x => new { x.OwnerUserId, x.SortOrder });
            b.Property(x => x.OwnerUserId).IsRequired();
            b.Property(x => x.DisplayMode).HasConversion<int>().IsRequired();
            b.Property(x => x.Kind).HasConversion<int>().IsRequired();
            b.Property(x => x.SortOrder).IsRequired();
            b.Property(x => x.Title).HasMaxLength(120);
            b.Property(x => x.PredefinedType).HasConversion<int?>();
             // Optional FK to ReportFavorite; on delete favorite -> cascade remove dependent KPIs
             b.HasOne<ReportFavorite>()
                 .WithMany()
                 .HasForeignKey(x => x.ReportFavoriteId)
                 .OnDelete(DeleteBehavior.Cascade);
         });
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.ConfigureWarnings(w => w.Ignore(RelationalEventId.NonTransactionalMigrationOperationWarning));
    }

    internal async Task ClearUserDataAsync(Guid userId, Action<int, int> progressCallback, CancellationToken ct)
    {
        var total = 21;
        var count = 0;

        // PostingAggregates (pro Dimension)
        await PostingAggregates
            .Where(p => p.AccountId != null && Accounts.Any(a => a.OwnerUserId == userId && a.Id == p.AccountId))
            .ExecuteDeleteAsync(ct);
        progressCallback(++count, total);

        await PostingAggregates
            .Where(p => p.ContactId != null && Contacts.Any(c => c.OwnerUserId == userId && c.Id == p.ContactId))
            .ExecuteDeleteAsync(ct);
        progressCallback(++count, total);

        await PostingAggregates
            .Where(p => p.SecurityId != null && Securities.Any(s => s.OwnerUserId == userId && s.Id == p.SecurityId))
            .ExecuteDeleteAsync(ct);
        progressCallback(++count, total);

        await PostingAggregates
            .Where(p => p.SavingsPlanId != null && SavingsPlans.Any(s => s.OwnerUserId == userId && s.Id == p.SavingsPlanId))
            .ExecuteDeleteAsync(ct);
        progressCallback(++count, total);

        // Postings (pro Dimension)
        await Postings
            .Where(p => p.AccountId != null && Accounts.Any(a => a.OwnerUserId == userId && a.Id == p.AccountId))
            .ExecuteDeleteAsync(ct);
        progressCallback(++count, total);

        await Postings
            .Where(p => p.ContactId != null && Contacts.Any(c => c.OwnerUserId == userId && c.Id == p.ContactId))
            .ExecuteDeleteAsync(ct);
        progressCallback(++count, total);

        await Postings
            .Where(p => p.SavingsPlanId != null && SavingsPlans.Any(s => s.OwnerUserId == userId && s.Id == p.SavingsPlanId))
            .ExecuteDeleteAsync(ct);
        progressCallback(++count, total);

        await Postings
            .Where(p => p.SecurityId != null && Securities.Any(s => s.OwnerUserId == userId && s.Id == p.SecurityId))
            .ExecuteDeleteAsync(ct);
        progressCallback(++count, total);

        // StatementEntries (korrekter Join auf StatementImports)
        await StatementEntries
            .Where(e => StatementImports
                .Any(i => Accounts.Any(a => a.OwnerUserId == userId && a.Id == i.AccountId) && e.StatementImportId == i.Id))
            .ExecuteDeleteAsync(ct);
        progressCallback(++count, total);

        // StatementImports
        await StatementImports
            .Where(i => Accounts.Any(a => a.OwnerUserId == userId && a.Id == i.AccountId))
            .ExecuteDeleteAsync(ct);
        progressCallback(++count, total);

        // StatementDraftEntries
        await StatementDraftEntries
            .Where(e => StatementDrafts.Any(d => d.Id == e.DraftId && d.OwnerUserId == userId))
            .ExecuteDeleteAsync(ct);
        progressCallback(++count, total);

        // StatementDrafts
        await StatementDrafts
            .Where(d => d.OwnerUserId == userId)
            .ExecuteDeleteAsync(ct);
        progressCallback(++count, total);

        // SavingsPlans
        await SavingsPlans
            .Where(s => s.OwnerUserId == userId)
            .ExecuteDeleteAsync(ct);
        progressCallback(++count, total);

        // SavingsPlanCategories
        await SavingsPlanCategories
            .Where(c => c.OwnerUserId == userId)
            .ExecuteDeleteAsync(ct);
        progressCallback(++count, total);

        // AliasNames (vor Contacts zur Sicherheit – alternativ würde FK-Cascade greifen)
        await AliasNames
            .Where(a => Contacts.Any(c => c.OwnerUserId == userId && c.Id == a.ContactId))
            .ExecuteDeleteAsync(ct);
        progressCallback(++count, total);

        // Contacts (ohne Self)
        await Contacts
            .Where(c => c.OwnerUserId == userId && c.Type != ContactType.Self)
            .ExecuteDeleteAsync(ct);
        Contacts.RemoveRange(Contacts.Where(c => c.OwnerUserId == userId && c.Type == ContactType.Self).Skip(1));
        await SaveChangesAsync(ct);
        progressCallback(++count, total);

        // ContactCategories
        await ContactCategories
            .Where(c => c.OwnerUserId == userId)
            .ExecuteDeleteAsync(ct);
        progressCallback(++count, total);

        // AccountShares (User direkt oder Accounts des Users)
        await AccountShares
            .Where(s => s.UserId == userId || Accounts.Any(a => a.OwnerUserId == userId && a.Id == s.AccountId))
            .ExecuteDeleteAsync(ct);
        progressCallback(++count, total);

        // Accounts
        await Accounts
            .Where(a => a.OwnerUserId == userId)
            .ExecuteDeleteAsync(ct);
        progressCallback(++count, total);

        // Securities (SecurityPrices werden per FK-Cascade entfernt)
        await Securities
            .Where(s => s.OwnerUserId == userId)
            .ExecuteDeleteAsync(ct);
        progressCallback(++count, total);

        // SecurityCategories
        await SecurityCategories
            .Where(c => c.OwnerUserId == userId)
            .ExecuteDeleteAsync(ct);
        progressCallback(++count, total);
    }

    // Bestehende sync-Methode (legacy) ruft neue Async-Variante
    internal void ClearUserData(Guid userId, Action<int, int> progressCallback)
        => ClearUserDataAsync(userId, progressCallback, CancellationToken.None).GetAwaiter().GetResult();
}
