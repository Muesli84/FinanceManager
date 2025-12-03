using FinanceManager.Shared;
using FinanceManager.Shared.Dtos.Users;
using FinanceManager.Web;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace FinanceManager.Tests.Integration.ApiClient;

public class ApiClientMetaHolidaysTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public ApiClientMetaHolidaysTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private FinanceManager.Shared.ApiClient CreateClient()
    {
        var http = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });
        return new FinanceManager.Shared.ApiClient(http);
    }
    private async Task EnsureAuthenticatedAsync(FinanceManager.Shared.ApiClient api)
    {
        var username = $"user_{Guid.NewGuid():N}";
        await api.Auth_RegisterAsync(new RegisterRequest(username, "Secret123", PreferredLanguage: null, TimeZoneId: null));
    }

    [Fact]
    public async Task Meta_HolidayProviders_Countries_Subdivisions_Work()
    {
        var api = CreateClient();
        await EnsureAuthenticatedAsync(api);
        // Providers
        var providers = await api.Meta_GetHolidayProvidersAsync();
        providers.Should().NotBeNull();
        providers.Should().Contain(p => !string.IsNullOrWhiteSpace(p));

        // Countries
        var countries = await api.Meta_GetHolidayCountriesAsync();
        countries.Should().NotBeNull();
        countries.Should().Contain("DE");

        // Subdivisions for valid provider + country
        // Use any provider returned, assume lowercase/uppercase tolerated by API
        var provider = providers.First();
        var subs = await api.Meta_GetHolidaySubdivisionsAsync(provider, "DE");
        subs.Should().NotBeNull();
        // may be empty depending on provider implementation, but call should succeed
    }
}
