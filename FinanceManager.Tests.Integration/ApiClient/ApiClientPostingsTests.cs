using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace FinanceManager.Tests.Integration.ApiClient;

public class ApiClientPostingsTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public ApiClientPostingsTests(TestWebApplicationFactory factory)
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
    public async Task Postings_GroupLinks_Should_Return_Null_For_Empty()
    {
        var api = CreateClient();
        await EnsureAuthenticatedAsync(api);
        var links = await api.Postings_GetGroupLinksAsync(Guid.Empty);
        links.Should().BeNull();
    }

    [Fact]
    public async Task Postings_List_Endpoints_Should_Not_Fail()
    {
        var api = CreateClient();
        await EnsureAuthenticatedAsync(api);

        // These will likely return empty due to no owned entities, but should succeed.
        var accList = await api.Postings_GetAccountAsync(Guid.NewGuid());
        accList.Should().NotBeNull();

        var conList = await api.Postings_GetContactAsync(Guid.NewGuid());
        conList.Should().NotBeNull();

        var spList = await api.Postings_GetSavingsPlanAsync(Guid.NewGuid());
        spList.Should().NotBeNull();

        var secList = await api.Postings_GetSecurityAsync(Guid.NewGuid());
        secList.Should().NotBeNull();
    }
}
