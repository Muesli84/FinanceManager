using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Vml;
using FinanceManager.Domain.Accounts;
using FinanceManager.Domain.Attachments; // new
using FinanceManager.Domain.Budget;
using FinanceManager.Domain.Contacts;
using FinanceManager.Domain.Notifications; // new
using FinanceManager.Domain.Portfolio;
using FinanceManager.Domain.Postings;
using FinanceManager.Domain.Reports; // added
using FinanceManager.Domain.Savings;
using FinanceManager.Domain.Securities;
using FinanceManager.Domain.Security; // new
using FinanceManager.Domain.Statements;
using FinanceManager.Domain.Users;
using FinanceManager.Infrastructure.Backups;
using FinanceManager.Infrastructure.Notifications; // new
using FinanceManager.Infrastructure.Statements;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using FinanceManager.Shared.Dtos.Budget;

namespace FinanceManager.Infrastructure;

/// <summary>
/// EF Core <see cref="DbContext"/> for the FinanceManager application.
/// Exposes DbSet properties for all domain aggregates and configures the model mappings.
/// Inherits from IdentityDbContext to include ASP.NET Identity user/role stores.
/// </summary>
public class AppDbContext : IdentityDbContext<User, IdentityRole<Guid>, Guid>
{
    /// <summary>
    /// Creates a new instance of <see cref="AppDbContext"/> using the provided options.
    /// </summary>
    /// <param name="options">The options to configure the context (provider, connection string, etc.).</param>
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    /// <summary>Bank accounts.</summary>
    /// <typeparam name="Account">The type parameter.</typeparam>
    /// <returns>The result.</returns>
    public DbSet<Account> Accounts => Set<Account>();
    /// <summary>Linked sub-IBANs for collection accounts.</summary>
    /// <typeparam name="AccountLinkedIban">The type parameter.</typeparam>
    /// <returns>The result.</returns>
    public DbSet<AccountLinkedIban> AccountLinkedIbans => Set<AccountLinkedIban>();
    /// <summary>Account sharing links.</summary>
    /// <typeparam name="AccountShare">The type parameter.</typeparam>
    /// <returns>The result.</returns>
    public DbSet<AccountShare> AccountShares => Set<AccountShare>();
    /// <summary>Contacts (counterparties, banks, self).</summary>
    /// <typeparam name="Contact">The type parameter.</typeparam>
    /// <returns>The result.</returns>
    public DbSet<Contact> Contacts => Set<Contact>();
    /// <summary>Contact categories.</summary>
    /// <typeparam name="ContactCategory">The type parameter.</typeparam>
    /// <returns>The result.</returns>
    public DbSet<ContactCategory> ContactCategories => Set<ContactCategory>();
    /// <summary>Alias names for contact matching.</summary>
    /// <typeparam name="AliasName">The type parameter.</typeparam>
    /// <returns>The result.</returns>
    public DbSet<AliasName> AliasNames => Set<AliasName>();
    /// <summary>Statement import batches metadata.</summary>
    /// <typeparam name="StatementImport">The type parameter.</typeparam>
    /// <returns>The result.</returns>
    public DbSet<StatementImport> StatementImports => Set<StatementImport>();
    /// <summary>Statement entry records created from imports.</summary>
    /// <typeparam name="StatementEntry">The type parameter.</typeparam>
    /// <returns>The result.</returns>
    public DbSet<StatementEntry> StatementEntries => Set<StatementEntry>();
    /// <summary>Postings (ledger entries).</summary>
    /// <typeparam name="Posting">The type parameter.</typeparam>
    /// <returns>The result.</returns>
    public DbSet<Posting> Postings => Set<Posting>();
    /// <summary>Statement import drafts.</summary>
    /// <typeparam name="StatementDraft">The type parameter.</typeparam>
    /// <returns>The result.</returns>
    public DbSet<StatementDraft> StatementDrafts => Set<StatementDraft>();
    /// <summary>Entries inside statement drafts.</summary>
    /// <typeparam name="StatementDraftEntry">The type parameter.</typeparam>
    /// <returns>The result.</returns>
    public DbSet<StatementDraftEntry> StatementDraftEntries => Set<StatementDraftEntry>();
    /// <summary>Durable booking guards preventing concurrent booking of one draft.</summary>
    /// <typeparam name="StatementDraftBookingGuard">The type parameter.</typeparam>
    /// <returns>The result.</returns>
    public DbSet<StatementDraftBookingGuard> StatementDraftBookingGuards => Set<StatementDraftBookingGuard>();
    /// <summary>Savings plans.</summary>
    /// <typeparam name="SavingsPlan">The type parameter.</typeparam>
    /// <returns>The result.</returns>
    public DbSet<SavingsPlan> SavingsPlans => Set<SavingsPlan>();
    /// <summary>Savings plan categories.</summary>
    public DbSet<SavingsPlanCategory> SavingsPlanCategories { get; set; } = null!;
    /// <summary>Securities / stocks.</summary>
    /// <returns>The result.</returns>
    public DbSet<FinanceManager.Domain.Securities.Security> Securities => Set<FinanceManager.Domain.Securities.Security>();
    /// <summary>Security categories.</summary>
    /// <typeparam name="SecurityCategory">The type parameter.</typeparam>
    /// <returns>The result.</returns>
    public DbSet<SecurityCategory> SecurityCategories => Set<SecurityCategory>();
    /// <summary>Aggregated posting values (pre-computed).</summary>
    /// <typeparam name="PostingAggregate">The type parameter.</typeparam>
    /// <returns>The result.</returns>
    public DbSet<PostingAggregate> PostingAggregates => Set<PostingAggregate>();
    /// <summary>Security historical prices.</summary>
    /// <typeparam name="SecurityPrice">The type parameter.</typeparam>
    /// <returns>The result.</returns>
    public DbSet<SecurityPrice> SecurityPrices => Set<SecurityPrice>();
    /// <summary>Backup records stored for the user.</summary>
    /// <typeparam name="BackupRecord">The type parameter.</typeparam>
    /// <returns>The result.</returns>
    public DbSet<BackupRecord> Backups => Set<BackupRecord>();
    /// <summary>Saved report favorites.</summary>
    /// <typeparam name="ReportFavorite">The type parameter.</typeparam>
    /// <returns>The result.</returns>
    public DbSet<ReportFavorite> ReportFavorites => Set<ReportFavorite>(); // new
    /// <summary>Home KPI configuration records.</summary>
    /// <typeparam name="HomeKpi">The type parameter.</typeparam>
    /// <returns>The result.</returns>
    public DbSet<HomeKpi> HomeKpis => Set<HomeKpi>(); // new
    /// <summary>Cached report data entries.</summary>
    /// <typeparam name="ReportCacheEntry">The type parameter.</typeparam>
    /// <returns>The result.</returns>
    public DbSet<ReportCacheEntry> ReportCacheEntries => Set<ReportCacheEntry>();
    /// <summary>Per-user portfolio analysis report KPI tile configuration.</summary>
    /// <typeparam name="PortfolioKpiConfiguration">The type parameter.</typeparam>
    /// <returns>The result.</returns>
    public DbSet<PortfolioKpiConfiguration> PortfolioKpiConfigurations => Set<PortfolioKpiConfiguration>();
    /// <summary>IP blocks for rate limiting / security.</summary>
    /// <typeparam name="IpBlock">The type parameter.</typeparam>
    /// <returns>The result.</returns>
    public DbSet<IpBlock> IpBlocks => Set<IpBlock>(); // new
    /// <summary>Security.txt settings.</summary>
    /// <typeparam name="SecurityTxtSettings">The type parameter.</typeparam>
    /// <returns>The result.</returns>
    public DbSet<SecurityTxtSettings> SecurityTxtSettings => Set<SecurityTxtSettings>();
    /// <summary>Notification entities for user notifications.</summary>
    /// <typeparam name="Notification">The type parameter.</typeparam>
    /// <returns>The result.</returns>
    public DbSet<Notification> Notifications => Set<Notification>(); // new
    /// <summary>Attachments stored in the database (binary or URL references).</summary>
    /// <typeparam name="Attachment">The type parameter.</typeparam>
    /// <returns>The result.</returns>
    public DbSet<Attachment> Attachments => Set<Attachment>(); // new
    /// <summary>Attachment categories.</summary>
    /// <typeparam name="AttachmentCategory">The type parameter.</typeparam>
    /// <returns>The result.</returns>
    public DbSet<AttachmentCategory> AttachmentCategories => Set<AttachmentCategory>(); // new
    /// <summary>Budget categories.</summary>
    /// <typeparam name="BudgetCategory">The type parameter.</typeparam>
    /// <returns>The result.</returns>
    public DbSet<BudgetCategory> BudgetCategories => Set<BudgetCategory>();
    /// <summary>Budget purposes.</summary>
    /// <typeparam name="BudgetPurpose">The type parameter.</typeparam>
    /// <returns>The result.</returns>
    public DbSet<BudgetPurpose> BudgetPurposes => Set<BudgetPurpose>();

    /// <summary>Budget rules.</summary>
    /// <typeparam name="BudgetRule">The type parameter.</typeparam>
    /// <returns>The result.</returns>
    public DbSet<BudgetRule> BudgetRules => Set<BudgetRule>();

    /// <summary>Budget overrides.</summary>
    /// <typeparam name="BudgetOverride">The type parameter.</typeparam>
    /// <returns>The result.</returns>
    public DbSet<BudgetOverride> BudgetOverrides => Set<BudgetOverride>();

    /// <summary>
    /// Configure the EF Core model: indexes, constraints and relationships.
    /// </summary>
    /// <param name="modelBuilder">The model builder to configure entities on. Must not be <c>null</c>.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="modelBuilder"/> is <c>null</c>.</exception>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<User>(b =>
        {
            b.HasIndex(x => x.UserName).IsUnique();
            b.Property(x => x.UserName).HasMaxLength(100).IsRequired();
            b.Property(x => x.PasswordHash).IsRequired();
            // Import split settings columns
            b.Property(x => x.ImportSplitMode).HasConversion<short>().IsRequired();
            b.Property(x => x.ImportMaxEntriesPerDraft).IsRequired();
            b.Property(x => x.ImportMonthlySplitThreshold);
            b.Property(x => x.MassImportDialogPolicy)
                .HasConversion<short>()
                .HasDefaultValue(FinanceManager.Shared.Dtos.Statements.MassImportDialogPolicy.OnMissingInformation)
                .HasSentinel(FinanceManager.Shared.Dtos.Statements.MassImportDialogPolicy.OnMissingInformation)
                .IsRequired();
            b.Property(x => x.KnownContactAutoCreateEnabled).HasDefaultValue(true).IsRequired();
            b.Property(x => x.AlphaVantageApiKey).HasMaxLength(2048);
            b.Property(x => x.ShareAlphaVantageApiKey).HasDefaultValue(false);
            b.Property(x => x.CacheKpisInLocalStorage).HasDefaultValue(false).IsRequired();
            // Return analysis settings
            b.Property(x => x.BenchmarkSecurityId);
            b.Property(x => x.ShowSharpeRatio).HasDefaultValue(false).IsRequired();
            b.Property(x => x.RiskFreeRate).HasPrecision(18, 6).HasDefaultValue(0m).IsRequired();
            b.HasOne<FinanceManager.Domain.Securities.Security>()
                .WithMany()
                .HasForeignKey(x => x.BenchmarkSecurityId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Account>(b =>
        {
            b.HasIndex(x => new { x.OwnerUserId, x.Name }).IsUnique();
            b.Property(x => x.Name).HasMaxLength(150).IsRequired();
            b.Property(x => x.Iban).HasMaxLength(34);
            // ✅ FIXED: Add unique index for IBAN (per user, only when IBAN is provided)
            b.HasIndex(x => new { x.OwnerUserId, x.Iban })
                .IsUnique()
                .HasFilter("[Iban] IS NOT NULL");
            // Symbol: optional attachment reference
            b.Property(x => x.SymbolAttachmentId);
            b.HasIndex(x => x.SymbolAttachmentId);

            // SavingsPlanExpectation mapping
            b.Property(x => x.SavingsPlanExpectation).HasConversion<short>().IsRequired();
            b.Property(x => x.SecurityProcessingEnabled).HasDefaultValue(true).IsRequired();
            b.Property(x => x.IsCollectionAccount).HasDefaultValue(false).IsRequired();
        });

        modelBuilder.Entity<AccountLinkedIban>(b =>
        {
            b.ToTable("AccountLinkedIbans");
            b.HasKey(x => x.Id);
            b.Property(x => x.Iban).HasMaxLength(34).IsRequired();
            b.HasIndex(x => new { x.AccountId, x.Iban }).IsUnique();
            b.HasOne(x => x.Account)
             .WithMany()
             .HasForeignKey(x => x.AccountId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Contact>(b =>
        {
            b.HasIndex(x => new { x.OwnerUserId, x.Name });
            // Ensure only a single Self contact per owner
            b.HasIndex(x => new { x.OwnerUserId, x.Type })
             .IsUnique()
             .HasFilter("[Type] = 0");
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
            b.HasIndex(x => new { x.SecurityId, x.BookingDate });
            b.Property(p => p.ParentId);
            b.Property(p => p.OriginalAmount).HasPrecision(18, 2);
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

        modelBuilder.Entity<FinanceManager.Domain.Securities.Security>(b =>
        {
            b.HasKey(x => x.Id);
            b.HasIndex(x => new { x.OwnerUserId, x.Name }).IsUnique();
            b.HasIndex(x => new { x.OwnerUserId, x.Identifier });
            b.Property(x => x.Name).HasMaxLength(200).IsRequired();
            b.Property(x => x.Identifier).HasMaxLength(50).IsRequired();
            b.Property(x => x.CurrencyCode).HasMaxLength(10).IsRequired();
            b.Property(x => x.AlphaVantageCode).HasMaxLength(50);
            b.Property(x => x.Region).HasMaxLength(255);
            b.Property(x => x.Sector).HasMaxLength(255);
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
            b.Property(x => x.Amount).HasPrecision(18, 2);
            b.Property(x => x.SecuritySubType);
            b.Property(x => x.DateKind);
            // broad unique index including subtype and date kind (only matters for security)
            b.HasIndex(x => new { x.Kind, x.AccountId, x.ContactId, x.SavingsPlanId, x.SecurityId, x.SecuritySubType, x.Period, x.PeriodStart, x.DateKind }).IsUnique();
            b.HasIndex(x => new { x.Kind, x.AccountId, x.Period, x.PeriodStart, x.DateKind })
                .IsUnique()
                .HasFilter("[AccountId] IS NOT NULL AND [ContactId] IS NULL AND [SavingsPlanId] IS NULL AND [SecurityId] IS NULL");
            b.HasIndex(x => new { x.Kind, x.ContactId, x.Period, x.PeriodStart, x.DateKind })
                .IsUnique()
                .HasFilter("[ContactId] IS NOT NULL AND [AccountId] IS NULL AND [SavingsPlanId] IS NULL AND [SecurityId] IS NULL");
            b.HasIndex(x => new { x.Kind, x.SavingsPlanId, x.Period, x.PeriodStart, x.DateKind })
                .IsUnique()
                .HasFilter("[SavingsPlanId] IS NOT NULL AND [AccountId] IS NULL AND [ContactId] IS NULL AND [SecurityId] IS NULL");
            b.HasIndex(x => new { x.Kind, x.SecurityId, x.SecuritySubType, x.Period, x.PeriodStart, x.DateKind })
                .IsUnique()
                .HasFilter("[SecurityId] IS NOT NULL AND [AccountId] IS NULL AND [ContactId] IS NULL AND [SavingsPlanId] IS NULL");
        });

        modelBuilder.Entity<StatementDraftEntry>(b =>
        {
            b.Property<Guid?>("SecurityId");
            b.Property<SecurityTransactionType?>("SecurityTransactionType");
            b.Property<decimal?>("SecurityQuantity").HasPrecision(18, 6);
            b.Property<decimal?>("SecurityFeeAmount").HasPrecision(18, 2);
            b.Property<decimal?>("SecurityTaxAmount").HasPrecision(18, 2);
            b.HasIndex("SecurityId");
        });

        modelBuilder.Entity<StatementDraftBookingGuard>(b =>
        {
            b.HasKey(x => x.Id);
            b.HasIndex(x => new { x.OwnerUserId, x.DraftId }).IsUnique();
            b.HasIndex(x => x.ExpiresUtc);
            b.Property(x => x.OwnerUserId).IsRequired();
            b.Property(x => x.DraftId).IsRequired();
            b.Property(x => x.LockToken).IsRequired();
            b.Property(x => x.AcquiredUtc).IsRequired();
            b.Property(x => x.ExpiresUtc).IsRequired();
            b.HasOne<StatementDraft>()
                .WithMany()
                .HasForeignKey(x => x.DraftId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SecurityPrice>(b =>
        {
            b.HasKey(x => x.Id);
            b.HasIndex(x => new { x.SecurityId, x.Date }).IsUnique();
            b.Property(x => x.Date).IsRequired();
            b.Property(x => x.Close).HasPrecision(18, 4).IsRequired();
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

        modelBuilder.Entity<ReportCacheEntry>(b =>
        {
            b.HasKey(x => x.Id);
            b.HasIndex(x => new { x.OwnerUserId, x.CacheKey }).IsUnique();
            b.Property(x => x.OwnerUserId).IsRequired();
            b.Property(x => x.CacheKey).HasMaxLength(120).IsRequired();
            b.Property(x => x.CacheValue).IsRequired();
            b.Property(x => x.NeedsRefresh).IsRequired();
            b.Property(x => x.Parameter).HasMaxLength(200).IsRequired();
            b.Property(x => x.CacheValidUntilUtc);
        });

        // PortfolioKpiConfiguration
        modelBuilder.Entity<PortfolioKpiConfiguration>(b =>
        {
            b.HasKey(x => x.Id);
            b.HasIndex(x => x.OwnerUserId).IsUnique();
            b.Property(x => x.OwnerUserId).IsRequired();
            b.Property(x => x.ActiveTileIds).IsRequired();
            b.Property(x => x.TileOrder).IsRequired();
            b.Property(x => x.UpdatedUtc).IsRequired();
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

        // IpBlock configuration
        modelBuilder.Entity<IpBlock>(b =>
        {
            b.HasKey(x => x.Id);
            b.HasIndex(x => x.IpAddress).IsUnique();
            b.Property(x => x.IpAddress).HasMaxLength(64).IsRequired();
        });

        // Notifications
        NotificationModelConfig.Configure(modelBuilder);

        // Attachments
        modelBuilder.Entity<Attachment>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.FileName).HasMaxLength(255).IsRequired();
            b.Property(x => x.ContentType).HasMaxLength(120).IsRequired();
            b.Property(x => x.Sha256).HasMaxLength(64);
            b.Property(x => x.Url).HasMaxLength(1000);
            b.Property(x => x.Note).HasMaxLength(500);
            b.Property(x => x.EntityKind).HasConversion<short>().IsRequired();
            b.HasIndex(x => new { x.OwnerUserId, x.EntityKind, x.EntityId });
            b.HasIndex(x => new { x.Sha256, x.OwnerUserId });
            b.HasOne<AttachmentCategory>()
                .WithMany()
                .HasForeignKey(x => x.CategoryId)
                .OnDelete(DeleteBehavior.Restrict);

            // NEW: reference to a master attachment (for dedup on postings)
            b.HasIndex(x => x.ReferenceAttachmentId);
            b.HasOne<Attachment>()
                .WithMany()
                .HasForeignKey(x => x.ReferenceAttachmentId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AttachmentCategory>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Name).HasMaxLength(150).IsRequired();
            b.Property(x => x.OwnerUserId).IsRequired();
            b.Property(x => x.IsSystem).IsRequired();
            b.HasIndex(x => new { x.OwnerUserId, x.Name }).IsUnique();
        });

        modelBuilder.Entity<BudgetCategory>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Name).HasMaxLength(150).IsRequired();
            b.HasIndex(x => new { x.OwnerUserId, x.Name }).IsUnique();
        });

        modelBuilder.Entity<BudgetPurpose>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Name).HasMaxLength(150).IsRequired();
            b.Property(x => x.Description).HasMaxLength(500);
            b.Property(x => x.SourceType).HasConversion<short>().IsRequired();
            b.Property(x => x.SourceId).IsRequired();
            b.Property(x => x.BudgetCategoryId);
            b.Property(x => x.ValuationType).HasConversion<short>().HasDefaultValue(BudgetValuationType.ExactPostings).IsRequired();
            b.HasIndex(x => new { x.OwnerUserId, x.Name });
            b.HasIndex(x => new { x.OwnerUserId, x.SourceType, x.SourceId });

            b.HasOne<BudgetCategory>()
                .WithMany()
                .HasForeignKey(x => x.BudgetCategoryId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<BudgetRule>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Amount).HasPrecision(18, 2);
            b.Property(x => x.Interval).HasConversion<short>().IsRequired();
            b.Property(x => x.StartDate).IsRequired();
            b.Property(x => x.EndDate);
            b.Property(x => x.BudgetPurposeId);
            b.Property(x => x.BudgetCategoryId);
            b.Property(x => x.PurposePattern).HasMaxLength(500);
            b.Property(x => x.PurposePatternIsRegex).HasDefaultValue(false).IsRequired();

            b.HasIndex(x => new { x.OwnerUserId, x.BudgetPurposeId });
            b.HasIndex(x => new { x.OwnerUserId, x.BudgetCategoryId });
            b.HasIndex(x => new { x.BudgetPurposeId, x.StartDate });
            b.HasIndex(x => new { x.BudgetCategoryId, x.StartDate });
        });

        modelBuilder.Entity<BudgetOverride>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Amount).HasPrecision(18, 2);
            b.Property(x => x.PeriodYear).IsRequired();
            b.Property(x => x.PeriodMonth).IsRequired();
            b.HasIndex(x => new { x.OwnerUserId, x.BudgetPurposeId, x.PeriodYear, x.PeriodMonth }).IsUnique();
        });
    }
    /// <summary>
    /// Configure warnings and other runtime options for the DbContext.
    /// </summary>
    /// <param name="optionsBuilder">The options builder to configure. Must not be <c>null</c>.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="optionsBuilder"/> is <c>null</c>.</exception>
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.ConfigureWarnings(w => w.Ignore(RelationalEventId.NonTransactionalMigrationOperationWarning));
    }

    /// <summary>
    /// Clears all user-specific data from the database. This operation is destructive and should be used with care.
    /// The method reports progress via the provided callback.
    /// </summary>
    /// <param name="userId">The user identifier whose data should be removed.</param>
    /// <param name="progressCallback">Callback invoked after each sub-step with (step, total).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes when the clear operation has finished.</returns>
    internal async Task ClearUserDataAsync(Guid userId, Action<int, int> progressCallback, CancellationToken ct)
    {
        var total = 26;
        var count = 0;

        // Notifications
        await Notifications
            .Where(n => n.OwnerUserId == userId)
            .ExecuteDeleteAsync(ct);
        progressCallback(++count, total);

        // AliasNames (vor Contacts zur Sicherheit – alternativ würde FK-Cascade greifen)
        await AliasNames
            .Where(a => Contacts.Any(c => c.OwnerUserId == userId && c.Id == a.ContactId))
            .ExecuteDeleteAsync(ct);
        progressCallback(++count, total);

        // AccountShares (User direkt oder Accounts des Users)
        await AccountShares
            .Where(s => s.UserId == userId || Accounts.Any(a => a.OwnerUserId == userId && a.Id == s.AccountId))
            .ExecuteDeleteAsync(ct);
        progressCallback(++count, total);

        // Report favorites
        await ReportFavorites
            .Where(r => r.OwnerUserId == userId)
            .ExecuteDeleteAsync(ct);
        progressCallback(++count, total);

        // Home Kpi
        await HomeKpis
            .Where(h => h.OwnerUserId == userId)
            .ExecuteDeleteAsync(ct);
        progressCallback(++count, total);

        // Posting aggregates (per dimension)
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

        // Postings (per dimension)
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

        // Security prices
        await SecurityPrices
            .Where(p => Securities.Any(s => s.OwnerUserId == userId && s.Id == p.SecurityId))
            .ExecuteDeleteAsync(ct);

        // Statement drafts
        await StatementDraftEntries
            .Where(e => StatementDrafts.Any(d => d.Id == e.DraftId && d.OwnerUserId == userId))
            .ExecuteDeleteAsync(ct);
        progressCallback(++count, total);

        await StatementDrafts
            .Where(d => d.OwnerUserId == userId)
            .ExecuteDeleteAsync(ct);
        progressCallback(++count, total);

        await StatementEntries
            .Where(e => StatementImports
                .Any(i => Accounts.Any(a => a.OwnerUserId == userId && a.Id == i.AccountId) && e.StatementImportId == i.Id))
            .ExecuteDeleteAsync(ct);
        progressCallback(++count, total);

        await StatementImports
            .Where(i => Accounts.Any(a => a.OwnerUserId == userId && a.Id == i.AccountId))
            .ExecuteDeleteAsync(ct);
        progressCallback(++count, total);

        // Accounts
        await Accounts
            .Where(a => a.OwnerUserId == userId)
            .ExecuteDeleteAsync(ct);
        progressCallback(++count, total);

        // Contacts (ohne Self)
        await Contacts
            .Where(c => c.OwnerUserId == userId && c.Type != ContactType.Self)
            .ExecuteDeleteAsync(ct);
        Contacts.RemoveRange(Contacts.Where(c => c.OwnerUserId == userId && c.Type == ContactType.Self).Skip(1));
        await SaveChangesAsync(ct);
        progressCallback(++count, total);

        // Savings plans
        await SavingsPlans
            .Where(s => s.OwnerUserId == userId)
            .ExecuteDeleteAsync(ct);
        progressCallback(++count, total);

        // Securities (SecurityPrices werden per FK-Cascade entfernt)
        await Securities
            .Where(s => s.OwnerUserId == userId)
            .ExecuteDeleteAsync(ct);
        progressCallback(++count, total);

        // Attachments
        await Attachments
            .Where(a => a.OwnerUserId == userId)
            .ExecuteDeleteAsync(ct);
        progressCallback(++count, total);

        // ContactCategories
        await ContactCategories
            .Where(c => c.OwnerUserId == userId)
            .ExecuteDeleteAsync(ct);
        progressCallback(++count, total);

        // AttachmentCategories
        await AttachmentCategories
            .Where(a => a.OwnerUserId == userId)
            .ExecuteDeleteAsync(ct);
        progressCallback(++count, total);

        // Savings plans categories
        await SavingsPlanCategories
            .Where(c => c.OwnerUserId == userId)
            .ExecuteDeleteAsync(ct);
        progressCallback(++count, total);

        // SecurityCategories
        await SecurityCategories
            .Where(c => c.OwnerUserId == userId)
            .ExecuteDeleteAsync(ct);
        progressCallback(++count, total);

        // Budgets
        var budgetPurposes = await BudgetPurposes.Where(x => x.OwnerUserId == userId).ToListAsync(ct);
        var budgetPurposeIds = budgetPurposes.Select(x => x.Id).ToList();

        var budgetRules = await BudgetRules
            .Where(x => x.OwnerUserId == userId)
            .Where(x => x.BudgetPurposeId != null && budgetPurposeIds.Contains(x.BudgetPurposeId.Value))
            .ToListAsync(ct);
        var budgetOverrides = await BudgetOverrides.Where(x => x.OwnerUserId == userId && budgetPurposeIds.Contains(x.BudgetPurposeId)).ToListAsync(ct);

        BudgetOverrides.RemoveRange(budgetOverrides);
        await SaveChangesAsync(ct);

        BudgetRules.RemoveRange(budgetRules);
        await SaveChangesAsync(ct);

        BudgetPurposes.RemoveRange(budgetPurposes);
        await SaveChangesAsync(ct);

        // Budget categories (delete last because purposes reference them)
        await BudgetCategories
            .Where(c => c.OwnerUserId == userId)
            .ExecuteDeleteAsync(ct);
        progressCallback(++count, total);
    }

}
