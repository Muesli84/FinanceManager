using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace FinanceManager.Tests.Integration.ApiClient;

public class ApiClientNotificationsTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public ApiClientNotificationsTests(TestWebApplicationFactory factory)
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
    public async Task Notifications_List_Then_Dismiss_Should_Succeed()
    {
        var api = CreateClient();
        await EnsureAuthenticatedAsync(api);

        var items = await api.Notifications_ListAsync();
        items.Should().NotBeNull();
        // Initially might be empty; we just validate the call.

        if (items.Count > 0)
        {
            var first = items.First();
            var ok = await api.Notifications_DismissAsync(first.Id);
            ok.Should().BeTrue();
        }
    }
}
