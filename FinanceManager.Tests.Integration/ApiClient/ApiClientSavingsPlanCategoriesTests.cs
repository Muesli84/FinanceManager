using FinanceManager.Shared;
using FinanceManager.Shared.Dtos.SavingsPlans;
using FinanceManager.Shared.Dtos.Users;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace FinanceManager.Tests.Integration.ApiClient;

public class ApiClientSavingsPlanCategoriesTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public ApiClientSavingsPlanCategoriesTests(TestWebApplicationFactory factory)
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
    public async Task SavingsPlanCategories_Flow_CRUD_And_Symbol()
    {
        var api = CreateClient();
        await EnsureAuthenticatedAsync(api);

        // initial list
        var list = await api.SavingsPlanCategories_ListAsync();
        list.Should().NotBeNull();

        // create
        var created = await api.SavingsPlanCategories_CreateAsync(new SavingsPlanCategoryDto { Name = "CatA" });
        created.Should().NotBeNull();
        created!.Name.Should().Be("CatA");

        // get
        var got = await api.SavingsPlanCategories_GetAsync(created.Id);
        got.Should().NotBeNull();
        got!.Id.Should().Be(created.Id);

        // update
        var updated = await api.SavingsPlanCategories_UpdateAsync(created.Id, new SavingsPlanCategoryDto { Name = "CatB" });
        updated.Should().NotBeNull();
        updated!.Name.Should().Be("CatB");

        // set/clear symbol (no actual attachment, just expect not found=false semantics)
        var setOk = await api.SavingsPlanCategories_SetSymbolAsync(created.Id, Guid.NewGuid());
        setOk.Should().BeTrue();
        var clearOk = await api.SavingsPlanCategories_ClearSymbolAsync(created.Id);
        clearOk.Should().BeTrue();

        // delete
        var del = await api.SavingsPlanCategories_DeleteAsync(created.Id);
        del.Should().BeTrue();
        var gone = await api.SavingsPlanCategories_GetAsync(created.Id);
        gone.Should().BeNull();
    }
}
