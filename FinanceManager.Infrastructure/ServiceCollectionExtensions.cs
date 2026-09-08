using FinanceManager.Application;
using FinanceManager.Application.Accounts;
using FinanceManager.Application.Aggregates;
using FinanceManager.Application.Attachments; // new
using FinanceManager.Application.Backups;
using FinanceManager.Application.Contacts;
using FinanceManager.Application.Demo;
using FinanceManager.Application.Notifications; // new
using FinanceManager.Application.Portfolio;
using FinanceManager.Application.Postings;
using FinanceManager.Application.Reports;
using FinanceManager.Application.Savings;
using FinanceManager.Application.Securities;
using FinanceManager.Application.Securities.ReturnAnalysis;
using FinanceManager.Application.Security; // new
using FinanceManager.Application.Setup;
using FinanceManager.Application.Statements;
using FinanceManager.Domain.Users;
using FinanceManager.Infrastructure.Accounts;
using FinanceManager.Infrastructure.Aggregates;
using FinanceManager.Infrastructure.Attachments; // new
using FinanceManager.Infrastructure.Auth;
using FinanceManager.Infrastructure.Backups;
using FinanceManager.Infrastructure.Contacts;
using FinanceManager.Infrastructure.Notifications; // new
using FinanceManager.Infrastructure.Portfolio;
using FinanceManager.Infrastructure.Postings;
using FinanceManager.Infrastructure.Reports;
using FinanceManager.Infrastructure.Savings;
using FinanceManager.Infrastructure.Securities;
using FinanceManager.Infrastructure.Securities.ReturnAnalysis;
using FinanceManager.Infrastructure.Security; // new
using FinanceManager.Infrastructure.Setup;
using FinanceManager.Infrastructure.Statements;
using FinanceManager.Infrastructure.Statements.Files;
using FinanceManager.Infrastructure.Statements.Parsers;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore; // required for RoleStore
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using FinanceManager.Application.Budget;
using FinanceManager.Infrastructure.Budget; // new
using System;

namespace FinanceManager.Infrastructure;

/// <summary>
/// Provides extension methods to register infrastructure services into the application's <see cref="IServiceCollection"/>.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the infrastructure dependencies (EF DbContext, repositories, services and related helpers) into the
    /// provided <see cref="IServiceCollection"/>.
    /// </summary>
    /// <param name="services">The service collection to register services on.</param>
    /// <param name="connectionString">
    /// Optional database connection string. When <c>null</c> a default SQLite database file
    /// ("financemanager.db") will be used.
    /// </param>
    /// <returns>The same <see cref="IServiceCollection"/> instance to allow fluent chaining.</returns>
    /// <remarks>
    /// The method registers scoped services for db context and other infrastructure services. Lifetimes follow the project's
    /// DI conventions: scoped for EF DbContext and services that depend on it. It also registers an Identity role store
    /// for <see cref="IdentityRole{Guid}"/> based roles (RoleManager is expected to be configured separately).
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is <c>null</c>.</exception>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string? connectionString)
    {
        services.AddDbContext<AppDbContext>(options =>
        {
            options.UseSqlite(connectionString ?? "Data Source=financemanager.db");
        });
        services.AddDbContextFactory<AppDbContext>(options =>
        {
            options.UseSqlite(connectionString ?? "Data Source=financemanager.db");
        }, ServiceLifetime.Scoped);

        services.AddSingleton<TimeProvider>(TimeProvider.System);
        // Register identity-compatible password hasher that delegates to legacy implementation
        services.AddScoped<Microsoft.AspNetCore.Identity.IPasswordHasher<User>, Pbkdf2IdentityPasswordHasher>();
        // Expose hashing helper for internal services
        services.AddScoped<IPasswordHashingService, Pbkdf2IdentityPasswordHasher>();

        services.AddScoped<IJwtTokenService, JwtTokenService>();
        services.AddScoped<FinanceManager.Application.Users.IUserAuthService, UserAuthService>();
        services.AddScoped<FinanceManager.Application.Users.IUserReadService, UserReadService>();
        services.AddScoped<FinanceManager.Application.Users.IUserAdminService, UserAdminService>();
        services.AddScoped<IContactService, ContactService>();
        services.AddSingleton<IKnownContactCatalog, KnownContactCatalog>();
        services.AddScoped<IContactCategoryService, ContactCategoryService>();
        services.AddScoped<ITimeZoneResolver, TimeZoneResolver>();
        services.AddScoped<IAccountStatisticsPeriodProvider, AccountStatisticsPeriodProvider>();
        services.AddScoped<IAccountService, AccountService>();
        services.AddScoped<IStatementDraftService, StatementDraftService>();
        services.AddScoped<IMassImportOrchestrator, MassImportOrchestrator>();
        services.AddScoped<IBudgetImpactEvaluationService, BudgetImpactEvaluationService>();
        services.AddScoped<IStatementFileParser, ING_CSV_StatementFileParser>();
        services.AddScoped<IStatementFileParser, ING_PDF_StatementFileParser>();
        services.AddScoped<IStatementFileParser, Barclays_PDF_StatementFileParser>();
        services.AddScoped<IStatementFileParser, Sparkasse_PDF_StatementFileParser>();
        services.AddScoped<IStatementFileParser, Wuestenrot_StatementFileParser>();
        services.AddScoped<IStatementFileParser, Backup_JSON_StatementFileParser>();
        services.AddScoped<ISavingsPlanService, SavingsPlanService>();
        services.AddScoped<ISavingsPlanCategoryService, SavingsPlanCategoryService>();
        services.AddScoped<ISetupImportService, SetupImportService>();
        services.AddScoped<ISecurityService, SecurityService>();
        services.AddScoped<ISecurityCategoryService, SecurityCategoryService>();
        services.AddScoped<IAutoInitializationService, AutoInitializationService>();
        services.AddScoped<IBackupService, BackupService>();
        services.AddScoped<IPostingAggregateService, PostingAggregateService>();
        services.AddScoped<IPostingTimeSeriesService, PostingTimeSeriesService>();
        services.AddScoped<IPostingReversalService, PostingReversalService>();
        services.AddScoped<IReportFavoriteService, ReportFavoriteService>();
        services.AddScoped<IReportAggregationService, ReportAggregationService>();
        services.AddScoped<IHomeKpiService, HomeKpiService>();
        services.AddScoped<IIpBlockService, IpBlockService>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<IAttachmentService, AttachmentService>();
        services.AddScoped<IAttachmentCategoryService, AttachmentCategoryService>();
        services.AddScoped<IPostingExportService, PostingExportService>();
        services.AddScoped<IDemoDataService, FinanceManager.Infrastructure.Demo.DemoDataService>();
        services.AddScoped<ISecurityPriceService, SecurityPriceService>();
        services.AddScoped<ISecurityPriceImportService, IngSecurityPriceImportService>();
        services.AddScoped<ISecurityPriceImportServiceFactory, SecurityPriceImportServiceFactory>();
        services.AddScoped<IStatementFile, Barclays_PDF_StatementFile>();
        services.AddScoped<IStatementFile, Sparkasse_PDF_StatementFile>();
        services.AddScoped<IStatementFile, ING_PDF_StatementFile>();
        services.AddScoped<IStatementFile, ING_Csv_StatementFile>();
        services.AddScoped<IStatementFile, Wuestenrot_PDF_StatementFile>();
        services.AddScoped<IStatementFile, Backup_JSON_StatementFile>();
        services.AddScoped<IStatementFileFactory>(sp => new StatementFileFactory(sp));
        services.AddScoped<IBudgetPlanningService, BudgetPlanningService>();
        services.AddScoped<IBudgetPurposeService, BudgetPurposeService>();
        services.AddScoped<IBudgetCategoryService, BudgetCategoryService>();
        services.AddScoped<IReportCacheService, ReportCacheService>();
        services.AddScoped<IBudgetReportService, BudgetReportService>();
        services.AddScoped<IBudgetRuleService, BudgetRuleService>();
        services.AddScoped<IBudgetOverrideService, BudgetOverrideService>();
        services.AddScoped<IBudgetPlanningRepository, BudgetPlanningRepository>();

        services.AddScoped<FinanceManager.Application.Common.IParentAssignmentService, FinanceManager.Infrastructure.Common.ParentAssignmentService>();

        // Register Identity RoleStore for Guid-based roles (RoleManager is registered by AddIdentity in Program.cs)
        services.AddScoped<IRoleStore<IdentityRole<Guid>>, RoleStore<IdentityRole<Guid>, AppDbContext, Guid>>();
        services.AddScoped<ISecurityReportService, SecurityReportService>();

        // Return analysis services
        services.AddMemoryCache(options => { options.SizeLimit = 10_000; });
        services.AddScoped<IReturnAnalysisService, ReturnAnalysisService>();
        services.AddSingleton<IReturnCalculationService, ReturnCalculationService>();
        services.AddSingleton<IFifoCostBasisCalculator, FifoCostBasisCalculator>();
        services.AddSingleton<IReturnAnalysisCache, MemoryReturnAnalysisCache>();

        // Portfolio analysis report services
        services.AddScoped<IPortfolioAnalysisReportService, PortfolioAnalysisReportService>();
        services.AddScoped<IPortfolioAnalysisReportCacheService, PortfolioAnalysisReportCacheService>();
        services.AddScoped<IPortfolioKpiConfigurationRepository, PortfolioKpiConfigurationRepository>();

        return services;
    }
}
