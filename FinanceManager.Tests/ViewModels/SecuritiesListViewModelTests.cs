using FinanceManager.Application;
using FinanceManager.Shared;
using FinanceManager.Shared.Dtos.Securities;
using FinanceManager.Web.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace FinanceManager.Tests.ViewModels;

public sealed class SecuritiesListViewModelTests
{
    private sealed class TestCurrentUserService : ICurrentUserService
    {
        public Guid UserId { get; set; } = Guid.NewGuid();
        public string? PreferredLanguage { get; set; }
        public bool IsAuthenticated { get; set; } = true;
        public bool IsAdmin { get; set; }
    }

    private static (SecuritiesListViewModel vm, Mock<IApiClient> apiMock) CreateVm(bool authenticated = true)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentUserService>(new TestCurrentUserService { IsAuthenticated = authenticated });
        var apiMock = new Mock<IApiClient>();
        services.AddSingleton(apiMock.Object);
        var sp = services.BuildServiceProvider();
        var vm = new SecuritiesListViewModel(sp);
        return (vm, apiMock);
    }

    [Fact]
    public async Task Initialize_Loads_List()
    {
        var (vm, apiMock) = CreateVm();
        var items = new List<SecurityDto>
        {
            new SecurityDto { Id = Guid.NewGuid(), Name = "A", Identifier = "A1" },
            new SecurityDto { Id = Guid.NewGuid(), Name = "B", Identifier = "B1" }
        };
        apiMock.Setup(a => a.Securities_ListAsync(true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(items);
        apiMock.Setup(a => a.SecurityCategories_ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SecurityCategoryDto>());

        await vm.InitializeAsync();

        Assert.True(vm.Loaded);
        Assert.Equal(2, vm.Items.Count);
    }

    [Fact]
    public async Task ToggleActive_Reloads()
    {
        var (vm, apiMock) = CreateVm();
        int calls = 0;
        apiMock.Setup(a => a.Securities_ListAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                calls++;
                return new List<SecurityDto>();
            });
        apiMock.Setup(a => a.SecurityCategories_ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SecurityCategoryDto>());

        await vm.InitializeAsync();
        Assert.Equal(1, calls);

        vm.ToggleActive();
        await Task.Delay(50);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task Initialize_RequiresAuth_When_NotAuthenticated()
    {
        var (vm, apiMock) = CreateVm(authenticated: false);
        bool authRequired = false;
        vm.AuthenticationRequired += (_, __) => authRequired = true;

        await vm.InitializeAsync();

        Assert.False(vm.Loaded);
        Assert.True(authRequired);
        apiMock.Verify(a => a.Securities_ListAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
