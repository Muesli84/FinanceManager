using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace FinanceManager.Tests.Integration.ApiClient;

public class ApiClientSecurityCategoriesTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public ApiClientSecurityCategoriesTests(TestWebApplicationFactory factory)
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
    public async Task SecurityCategories_List_Create_Update_Symbol_Delete_Flow()
    {
        var api = CreateClient();
        await EnsureAuthenticatedAsync(api);

        // initial list
        var list = await api.SecurityCategories_ListAsync();
        list.Should().NotBeNull();
        list.Should().BeEmpty();

        // create
        var created = await api.SecurityCategories_CreateAsync(new SecurityCategoryRequest { Name = "Tech" });
        created.Should().NotBeNull();
        created.Name.Should().Be("Tech");

        // get by id
        var got = await api.SecurityCategories_GetAsync(created.Id);
        got.Should().NotBeNull();
        got!.Id.Should().Be(created.Id);
        got!.Name.Should().Be("Tech");

        // update name
        var updated = await api.SecurityCategories_UpdateAsync(created.Id, new SecurityCategoryRequest { Name = "Technology" });
        updated.Should().NotBeNull();
        updated!.Name.Should().Be("Technology");

        // set symbol with non-existent attachment -> should be false (NotFound)
        var setOk = await api.SecurityCategories_SetSymbolAsync(created.Id, Guid.NewGuid());
        setOk.Should().BeFalse();
        // clear symbol -> should be true even if none set
        var clearOk = await api.SecurityCategories_ClearSymbolAsync(created.Id);
        clearOk.Should().BeTrue();

        // delete
        var delOk = await api.SecurityCategories_DeleteAsync(created.Id);
        delOk.Should().BeTrue();
        var gone = await api.SecurityCategories_GetAsync(created.Id);
        gone.Should().BeNull();
    }
}
