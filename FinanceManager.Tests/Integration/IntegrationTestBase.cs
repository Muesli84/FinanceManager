using DocumentFormat.OpenXml.Office2016.Drawing.ChartDrawing;
using FinanceManager.Infrastructure;
using FinanceManager.Infrastructure.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Net.Http.Headers;

namespace FinanceManager.Tests.Integration;

/// <summary>
/// Base class for integration tests that need a configured test HttpClient and ApiClient helpers.
/// Derived test classes should accept a <see cref="WebApplicationFactory{Program}"/> in their constructor
/// and pass it to the base ctor: <c>public MyTests(WebApplicationFactory&lt;Program&gt; factory) : base(factory) { }</c>
/// </summary>
public abstract class IntegrationTestBase
{
    protected readonly WebApplicationFactory<Program> Factory;

    protected IntegrationTestBase(WebApplicationFactory<Program> factory)
    {
        Factory = factory;
    }

    /// <summary>
    /// Creates an HttpClient configured for the test host. Optionally allows
    /// customization of the service collection before the host starts (e.g.
    /// to replace services with mocks or test implementations).
    /// The returned client is preconfigured with a valid JWT Authorization header.
    /// </summary>
    /// <param name="serviceConfiguration">Optional action to customize test service registrations.</param>
    /// <returns>Configured <see cref="HttpClient"/> for the test server.</returns>
    protected HttpClient CreateHttpClient(bool useMemoryDb = false, Action<IServiceCollection>? serviceConfiguration = null)
    {
        var clientFactory = Factory.WithWebHostBuilder(builder =>
        {
            // disable background tasks for tests
            builder.ConfigureAppConfiguration((context, conf) =>
            {
                conf.AddInMemoryCollection(new[] { new KeyValuePair<string, string>("BackgroundTasks:Enabled", "false") });
            });

            // disable file logging in tests
            builder.ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddConsole();
            });

            // allow tests to replace services
            builder.ConfigureTestServices(services => 
            {
                if (useMemoryDb)
                {
                    // Use a dedicated in-memory SQLite database for tests to avoid touching the
                    // production file and to prevent duplicate-migration / duplicate-column errors.
                    var connection = new SqliteConnection("DataSource=:memory:");
                    connection.Open();

                    // Replace AppDbContext registration with in-memory SQLite
                    services.AddSingleton<SqliteConnection>(connection);
                    services.AddDbContext<AppDbContext>(options =>
                    {
                        options.UseSqlite(connection);
                    });
                }

                serviceConfiguration?.Invoke(services); 
            });
        });

        var client = clientFactory.CreateClient();

        // attach a JWT for authentication using the app's registered IJwtTokenService
        using var scope = clientFactory.Services.CreateScope();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var token = jwt.CreateToken(Guid.NewGuid(), "testuser", isAdmin: false, out var expires);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return client;
    }

    /// <summary>
    /// Helper that creates an ApiClient backed by a test HttpClient using optional service registration customization.
    /// </summary>
    protected FinanceManager.Web.Services.ApiClient CreateApiClient(bool useMemoryDb = false, Action<IServiceCollection>? serviceConfiguration = null)
    {
        var client = CreateHttpClient(useMemoryDb, serviceConfiguration);
        return new FinanceManager.Web.Services.ApiClient(client);
    }
}
