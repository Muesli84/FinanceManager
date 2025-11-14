using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;
using FinanceManager.Domain.Notifications;
using FinanceManager.Application.Notifications;
using System.Threading;
using Microsoft.Data.Sqlite;
using FinanceManager.Infrastructure; // for AppDbContext
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.Net.Http.Headers;
using Microsoft.Extensions.Configuration;
using FinanceManager.Infrastructure.Auth;
using System.Collections.Generic; // added
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Hosting; // added

namespace FinanceManager.Tests.Integration;

public class MetaHolidayRoutesTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public MetaHolidayRoutesTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private HttpClient CreateTestClient(Action<IServiceCollection>? serviceConfiguration = null)
    {
        var clientFactory = _factory.WithWebHostBuilder(builder =>
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

            // no special configuration here; use app's registered JwtTokenService
            builder.ConfigureTestServices(services => { serviceConfiguration?.Invoke(services); });
        });

        // create client (this starts the test server)
        var client = clientFactory.CreateClient();

        // obtain the app's IJwtTokenService from the test host and create a token
        using var scope = clientFactory.Services.CreateScope();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var token = jwt.CreateToken(Guid.NewGuid(), "testuser", isAdmin: false, out var expires);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return client;
    }

    private HttpClient CreateClientWithMocks(Mock<IHolidaySubdivisionService> subdivisionMock)
    {
        return CreateTestClient((services) => {
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

            services.AddSingleton<IHolidaySubdivisionService>(subdivisionMock.Object);
        });
    }

    [Fact]
    public async Task HolidayCountries_ShouldReturn200AndList()
    {
        var client = CreateTestClient();
        var resp = await client.GetAsync("/api/meta/holiday-countries");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var list = await resp.Content.ReadFromJsonAsync<string[]>();
        Assert.NotNull(list);
        Assert.Contains("DE", list);
    }

    [Fact]
    public async Task HolidayProviders_ShouldReturnEnumNames()
    {
        var client = CreateTestClient();
        var resp = await client.GetAsync("/api/meta/holiday-providers");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var providers = await resp.Content.ReadFromJsonAsync<string[]>();
        Assert.Contains(nameof(HolidayProviderKind.NagerDate), providers);
    }

    [Fact]
    public async Task HolidaySubdivisions_ShouldCallService()
    {
        var mock = new Mock<IHolidaySubdivisionService>();
        mock.Setup(s => s.GetSubdivisionsAsync(HolidayProviderKind.Memory, "DE", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { "BY", "BE" });

        var client = CreateClientWithMocks(mock);
        var resp = await client.GetAsync("/api/meta/holiday-subdivisions?provider=Memory&country=DE");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var list = await resp.Content.ReadFromJsonAsync<string[]>();
        Assert.Equal(new[] { "BY", "BE" }, list);
        mock.VerifyAll();
    }
}
