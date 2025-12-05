using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace FinanceManager.Tests.Integration.ApiClient;

public class ApiClientContactsTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public ApiClientContactsTests(TestWebApplicationFactory factory)
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
        await api.Auth_RegisterAsync(new FinanceManager.Shared.Dtos.Users.RegisterRequest(username, "Secret123", PreferredLanguage: null, TimeZoneId: null));
    }

    [Fact]
    public async Task Contacts_List_Create_Get_Update_Delete_Flow()
    {
        var api = CreateClient();
        await EnsureAuthenticatedAsync(api);

        // list initially contains auto-created Self contact
        var list = await api.Contacts_ListAsync(skip: 0, take: 10);
        list.Should().NotBeNull();
        list.Should().NotBeEmpty();
        list.Should().ContainSingle(c => c.Type == ContactType.Self);
        var initialCount = list.Count;

        // create
        var created = await api.Contacts_CreateAsync(new ContactCreateRequest("Test", ContactType.Bank, null, null, false));
        created.Should().NotBeNull();
        created.Name.Should().Be("Test");

        // get by id
        var got = await api.Contacts_GetAsync(created.Id);
        got.Should().NotBeNull();
        got!.Id.Should().Be(created.Id);

        // update
        var updated = await api.Contacts_UpdateAsync(created.Id, new ContactUpdateRequest("Test2", ContactType.Bank, null, null, false));
        updated.Should().NotBeNull();
        updated!.Name.Should().Be("Test2");

        // aliases
        var addOk = await api.Contacts_AddAliasAsync(created.Id, new AliasCreateRequest("PATTERN"));
        addOk.Should().BeTrue();
        var aliases = await api.Contacts_GetAliasesAsync(created.Id);
        aliases.Should().NotBeNull();

        // count should be at least initialCount
        var count = await api.Contacts_CountAsync();
        count.Should().BeGreaterOrEqualTo(initialCount);

        // delete
        var delOk = await api.Contacts_DeleteAsync(created.Id);
        delOk.Should().BeTrue();
        var gone = await api.Contacts_GetAsync(created.Id);
        gone.Should().BeNull();
    }
}
