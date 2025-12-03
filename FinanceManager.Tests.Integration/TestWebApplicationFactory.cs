using System.Data.Common;
using FinanceManager.Infrastructure;
using FinanceManager.Web;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting; // for IHostedService
using FinanceManager.Application; // BackgroundTaskRunner
using FinanceManager.Web.Services; // SecurityPriceWorker

namespace FinanceManager.Tests.Integration;

// Custom factory that wires AppDbContext to a fresh SQLite in-memory database per factory instance
public sealed class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    private DbConnection? _connection;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        // Disable background hosted services for integration tests via configuration flags
        builder.ConfigureAppConfiguration((ctx, cfg) =>
        {
            var overrides = new Dictionary<string, string?>
            {
                ["BackgroundTasks:Enabled"] = "false",
                ["Workers:SecurityPriceWorker:Enabled"] = "false",
                ["FileLogging:Enabled"] = "false"
            };
            cfg.AddInMemoryCollection(overrides);
        });

        builder.ConfigureServices(services =>
        {
            // Remove specific hosted services so they do not start in tests
            var hostedToRemove = services
                .Where(d => d.ServiceType == typeof(IHostedService) &&
                            (d.ImplementationType == typeof(BackgroundTaskRunner)
                             || d.ImplementationType == typeof(SecurityPriceWorker)
                             || (d.ImplementationType?.Name == "MonthlyReminderScheduler")))
                .ToList();
            foreach (var d in hostedToRemove)
            {
                services.Remove(d);
            }

            // Remove existing AppDbContext registration
            var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
            if (descriptor != null)
            {
                services.Remove(descriptor);
            }

            // Create and open in-memory SQLite connection (shared cache to support multiple contexts)
            _connection = new SqliteConnection("DataSource=:memory:;Cache=Shared");
            _connection.Open();

            services.AddDbContext<AppDbContext>(options =>
            {
                options.UseSqlite(_connection);
            });

            // Ensure schema is created for the fresh database
            using var sp = services.BuildServiceProvider();
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Database.EnsureDeleted();
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _connection?.Dispose();
            _connection = null;
        }
    }
}
