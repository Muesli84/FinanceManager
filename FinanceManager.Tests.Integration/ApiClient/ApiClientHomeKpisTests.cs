using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace FinanceManager.Tests.Integration.ApiClient;

public class ApiClientHomeKpisTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public ApiClientHomeKpisTests(TestWebApplicationFactory factory)
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
    public async Task HomeKpis_List_Create_Update_Delete_Flow()
    {
        var api = CreateClient();
        await EnsureAuthenticatedAsync(api);

        // list initially empty
        var list = await api.HomeKpis_ListAsync();
        list.Should().NotBeNull();
        list.Should().BeEmpty();

        // create predefined
        var created = await api.HomeKpis_CreateAsync(new HomeKpiCreateRequest(HomeKpiKind.Predefined, null, HomeKpiPredefined.AccountsAggregates, null, HomeKpiDisplayMode.TotalOnly, 0));
        created.Should().NotBeNull();
        created.Kind.Should().Be(HomeKpiKind.Predefined);

        // get by id
        var got = await api.HomeKpis_GetAsync(created.Id);
        got.Should().NotBeNull();
        got!.Id.Should().Be(created.Id);

        // update sort (title optional may remain null)
        var updated = await api.HomeKpis_UpdateAsync(created.Id, new HomeKpiUpdateRequest(created.Kind, created.ReportFavoriteId, created.PredefinedType, "New Title", created.DisplayMode, 1));
        updated.Should().NotBeNull();
        updated!.Title.Should().Be("New Title");
        updated!.SortOrder.Should().Be(1);

        // delete
        var delOk = await api.HomeKpis_DeleteAsync(created.Id);
        delOk.Should().BeTrue();
        var gone = await api.HomeKpis_GetAsync(created.Id);
        gone.Should().BeNull();
    }
}
