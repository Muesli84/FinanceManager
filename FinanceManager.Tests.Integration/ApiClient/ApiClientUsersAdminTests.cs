using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace FinanceManager.Tests.Integration.ApiClient;

public class ApiClientUsersAdminTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public ApiClientUsersAdminTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private FinanceManager.Shared.ApiClient CreateClient()
    {
        var http = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        return new FinanceManager.Shared.ApiClient(http);
    }

    [Fact]
    public async Task Admin_CreateListUpdateDelete_User()
    {
        var api = CreateClient();
        // Create a bootstrap admin via registration
        var adminUser = $"admin_{Guid.NewGuid():N}";
        await api.Auth_RegisterAsync(new RegisterRequest(adminUser, "Secret123", null, null));

        // Create user (min length >= 3)
        var created = await api.Admin_CreateUserAsync(new CreateUserRequest("user1", "Secret123", IsAdmin: false));
        created.Username.Should().Be("user1");

        // List contains new user
        var users = await api.Admin_ListUsersAsync();
        users.Should().Contain(u => u.Username == "user1");

        // Update
        var updated = await api.Admin_UpdateUserAsync(created.Id, new UpdateUserRequest("user1x", false, true, null));
        updated!.Username.Should().Be("user1x");
        updated.Active.Should().BeTrue();

        // Reset password
        var okReset = await api.Admin_ResetPasswordAsync(created.Id, new ResetPasswordRequest("Newpass123"));
        okReset.Should().BeTrue();

        // Unlock
        var okUnlock = await api.Admin_UnlockUserAsync(created.Id);
        okUnlock.Should().BeTrue();

        // Delete
        var okDel = await api.Admin_DeleteUserAsync(created.Id);
        okDel.Should().BeTrue();
    }
}
