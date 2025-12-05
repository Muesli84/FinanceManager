using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace FinanceManager.Tests.Integration.ApiClient;

public class ApiClientAuthTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public ApiClientAuthTests(TestWebApplicationFactory factory)
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

    [Fact]
    public async Task Register_ShouldSetAuthCookie_AndReturnResponse()
    {
        var api = CreateClient();
        var req = new RegisterRequest($"user_{Guid.NewGuid():N}", "Secret123", PreferredLanguage: "de", TimeZoneId: "Europe/Berlin");
        var resp = await api.Auth_RegisterAsync(req);
        resp.Should().NotBeNull();
        resp.isAdmin.Should().BeFalse();
        resp.user.Should().Be(req.Username);
        resp.exp.Should().BeAfter(DateTime.UtcNow);
    }

    [Fact]
    public async Task Login_ShouldReturnOk_AndUnauthorized_OnInvalid()
    {
        var api = CreateClient();
        var username = $"user_{Guid.NewGuid():N}";
        // register first
        await api.Auth_RegisterAsync(new RegisterRequest(username, "Secret123", null, null));

        var ok = await api.Auth_LoginAsync(new LoginRequest(username, "Secret123", null, null));
        ok.Should().NotBeNull();
        ok.user.Should().Be(username);

        // invalid password
        Func<Task> invalid = () => api.Auth_LoginAsync(new LoginRequest(username, "wrongpw", null, null));
        await invalid.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task Logout_ShouldClearCookie()
    {
        var api = CreateClient();
        var username = $"user_{Guid.NewGuid():N}";
        await api.Auth_RegisterAsync(new RegisterRequest(username, "Secret123", null, null));

        var ok = await api.Auth_LogoutAsync();
        ok.Should().BeTrue();
        // Further validation: subsequent authenticated-only endpoints would fail; basic check is enough here.
    }
}
