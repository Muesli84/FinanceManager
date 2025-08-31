using FinanceManager.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using FinanceManager.Application;
using FinanceManager.Infrastructure.Auth;

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
        return services;
    }
}
