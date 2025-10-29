using FinanceManager.Application;
using FinanceManager.Application.Accounts;
using FinanceManager.Application.Contacts;
using FinanceManager.Application.Savings;
using FinanceManager.Application.Securities;
using FinanceManager.Application.Statements;
using FinanceManager.Domain;
using FinanceManager.Infrastructure.Accounts;
using FinanceManager.Infrastructure.Auth;
using FinanceManager.Infrastructure.Contacts;
using FinanceManager.Infrastructure.Savings;
using FinanceManager.Infrastructure.Securities;
using FinanceManager.Infrastructure.Setup;
using FinanceManager.Infrastructure.Statements;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using FinanceManager.Application.Backups;
using FinanceManager.Infrastructure.Backups;
using FinanceManager.Application.Aggregates;
using FinanceManager.Infrastructure.Aggregates;
using FinanceManager.Application.Reports;
using FinanceManager.Infrastructure.Reports;
using FinanceManager.Application.Security; // new
using FinanceManager.Infrastructure.Security; // new
using FinanceManager.Application.Notifications; // new
using FinanceManager.Infrastructure.Notifications; // new
using FinanceManager.Application.Attachments; // new
using FinanceManager.Infrastructure.Attachments; // new
using Microsoft.AspNetCore.Identity;
using FinanceManager.Domain.Users;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore; // required for RoleStore

namespace FinanceManager.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string? connectionString)
    {
        services.AddDbContext<AppDbContext>(options =>
        {
            options.UseSqlite(connectionString ?? "Data Source=financemanager.db");
        });

        services.AddScoped<IDateTimeProvider, SystemDateTimeProvider>();
        // Register identity-compatible password hasher that delegates to legacy implementation
        services.AddScoped<Microsoft.AspNetCore.Identity.IPasswordHasher<User>, Pbkdf2IdentityPasswordHasher>();
        // Expose hashing helper for internal services
        services.AddScoped<IPasswordHashingService, Pbkdf2IdentityPasswordHasher>();

        services.AddScoped<IJwtTokenService, JwtTokenService>();
        services.AddScoped<FinanceManager.Application.Users.IUserAuthService, UserAuthService>();
        services.AddScoped<FinanceManager.Application.Users.IUserReadService, UserReadService>();
        services.AddScoped<FinanceManager.Application.Users.IUserAdminService, UserAdminService>();
        services.AddScoped<IContactService, ContactService>();
        services.AddScoped<IContactCategoryService, ContactCategoryService>();
        services.AddScoped<IAccountService, AccountService>();
        services.AddScoped<IStatementDraftService, StatementDraftService>();
        services.AddScoped<ISavingsPlanService, SavingsPlanService>();
        services.AddScoped<ISavingsPlanCategoryService, SavingsPlanCategoryService>();
        services.AddScoped<ISetupImportService, SetupImportService>();
        services.AddScoped<ISecurityService, SecurityService>();
        services.AddScoped<ISecurityCategoryService, SecurityCategoryService>();
        services.AddScoped< IAutoInitializationService , AutoInitializationService>();
        services.AddScoped<IBackupService, BackupService>();
        services.AddScoped<IPostingAggregateService, PostingAggregateService>();
        services.AddScoped<IPostingTimeSeriesService, PostingTimeSeriesService>();
        services.AddScoped<IReportFavoriteService, ReportFavoriteService>();
        services.AddScoped<IReportAggregationService, ReportAggregationService>();
        services.AddScoped<IHomeKpiService, HomeKpiService>();
        services.AddScoped<IIpBlockService, IpBlockService>(); // new
        services.AddScoped<INotificationService, NotificationService>(); // new
        services.AddScoped<IAttachmentService, AttachmentService>(); // new
        services.AddScoped<IAttachmentCategoryService, AttachmentCategoryService>(); // new
        services.AddScoped<IPostingExportService, PostingExportService>(); // new

        // Register Identity RoleStore for Guid-based roles (RoleManager is registered by AddIdentity in Program.cs)
        services.AddScoped<IRoleStore<IdentityRole<Guid>>, RoleStore<IdentityRole<Guid>, AppDbContext, Guid>>();

        return services;
    }
}
