using FinanceManager.Shared;
using FinanceManager.Shared.Dtos.Security;
using FinanceManager.Web;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;
using System.Net;

namespace FinanceManager.Tests.Integration.ApiClient;

public class ApiClientIpBlocksTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public ApiClientIpBlocksTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private FinanceManager.Shared.ApiClient CreateClient()
    {
        var http = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        return new FinanceManager.Shared.ApiClient(http);
    }

    [Fact]
    public async Task IpBlocks_List_Create_Block_Unblock_Delete()
    {
        var api = CreateClient();
        // Ensure admin context by registering first user
        var adminUser = $"admin_{Guid.NewGuid():N}";
        var password = "Secret123"; // satisfies uppercase + digit + length
        await api.Auth_RegisterAsync(new Shared.Dtos.Users.RegisterRequest(adminUser, password, null, null));

        var list = await api.Admin_ListIpBlocksAsync();
        list.Should().NotBeNull();

        // Create
        var created = await api.Admin_CreateIpBlockAsync(new IpBlockCreateRequest("1.2.3.4", "test", IsBlocked: false));
        created.IpAddress.Should().Be("1.2.3.4");
        created.IsBlocked.Should().BeFalse();

        // Update (block)
        var updated = await api.Admin_UpdateIpBlockAsync(created.Id, new IpBlockUpdateRequest("changed", IsBlocked: true));
        updated!.IsBlocked.Should().BeTrue();

        // Block explicitly
        var okBlock = await api.Admin_BlockIpAsync(created.Id, "now block", CancellationToken.None);
        okBlock.Should().BeTrue();

        // Unblock
        var okUnblock = await api.Admin_UnblockIpAsync(created.Id);
        okUnblock.Should().BeTrue();

        // Reset counters
        var okReset = await api.Admin_ResetCountersAsync(created.Id);
        okReset.Should().BeTrue();

        // Delete
        var okDel = await api.Admin_DeleteIpBlockAsync(created.Id);
        okDel.Should().BeTrue();
    }
}
