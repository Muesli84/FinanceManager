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
using FinanceManager.Infrastructure.Statements;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

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
        services.AddScoped<IPasswordHasher, Pbkdf2PasswordHasher>();
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
        return services;
    }
}
