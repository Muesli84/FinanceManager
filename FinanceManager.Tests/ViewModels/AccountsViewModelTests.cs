using FinanceManager.Application;
using FinanceManager.Shared;
using FinanceManager.Shared.Dtos.Accounts;
using FinanceManager.Web.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Moq;

namespace FinanceManager.Tests.ViewModels;

public sealed class AccountsViewModelTests
{
    private sealed class TestCurrentUserService : ICurrentUserService
    {
        public Guid UserId => Guid.NewGuid();
        public string? PreferredLanguage => "de";
        public bool IsAuthenticated { get; set; } = true;
        public bool IsAdmin => false;
    }

    private sealed class DummyLocalizer : IStringLocalizer
    {
        public LocalizedString this[string name] => new(name, name);
        public LocalizedString this[string name, params object[] arguments] => new(name, name);
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => Array.Empty<LocalizedString>();
    }

    private static (AccountsViewModel vm, Mock<IApiClient> apiMock) CreateVm(bool isAuthenticated = true)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentUserService>(new TestCurrentUserService { IsAuthenticated = isAuthenticated });
        var apiMock = new Mock<IApiClient>();
        services.AddSingleton(apiMock.Object);
        var sp = services.BuildServiceProvider();
        var vm = new AccountsViewModel(sp);
        return (vm, apiMock);
    }

    [Fact]
    public async Task Initialize_LoadsAccounts_WhenAuthenticated()
    {
        var (vm, apiMock) = CreateVm(isAuthenticated: true);
        var accounts = new List<AccountDto>
        {
            new AccountDto(Guid.NewGuid(), "A", AccountType.Giro, "DE00", 10m, Guid.NewGuid(), null, SavingsPlanExpectation.Optional),
            new AccountDto(Guid.NewGuid(), "B", AccountType.Savings, null, 20m, Guid.NewGuid(), null, SavingsPlanExpectation.Optional)
        };
        apiMock.Setup(a => a.GetAccountsAsync(0, 100, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(accounts);

        await vm.InitializeAsync();

        Assert.True(vm.Loaded);
        Assert.Equal(2, vm.Accounts.Count);
        Assert.All(vm.Accounts, a => Assert.False(string.IsNullOrWhiteSpace(a.Name)));
    }

    [Fact]
    public async Task Initialize_RequiresAuth_WhenNotAuthenticated()
    {
        var (vm, apiMock) = CreateVm(isAuthenticated: false);
        var authEvents = 0;
        vm.AuthenticationRequired += (_, __) => authEvents++;

        await vm.InitializeAsync();

        Assert.Equal(1, authEvents);
        Assert.False(vm.Loaded);
        apiMock.Verify(a => a.GetAccountsAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SetFilter_AffectsLoad_AndRibbon()
    {
        var (vm, apiMock) = CreateVm();
        var filterId = Guid.NewGuid();
        apiMock.Setup(a => a.GetAccountsAsync(0, 100, filterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AccountDto>());

        vm.SetFilter(filterId);
        await vm.InitializeAsync();

        apiMock.Verify(a => a.GetAccountsAsync(0, 100, filterId, It.IsAny<CancellationToken>()), Times.Once);

        var loc = new DummyLocalizer();
        var groups = vm.GetRibbon(loc);
        Assert.Contains(groups, g => g.Items.Any(i => i.Action == "ClearFilter"));
    }

    [Fact]
    public void GetRibbon_ContainsNew()
    {
        var (vm, _) = CreateVm();
        var loc = new DummyLocalizer();
        var groups = vm.GetRibbon(loc);
        Assert.Contains(groups, g => g.Items.Any(i => i.Action == "New"));
    }
}
