using FinanceManager.Application;
using FinanceManager.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Moq;

namespace FinanceManager.Tests.ViewModels;

/// <summary>
/// Covers <c>BankAccountListViewModel</c> loading behavior, authentication gating, search/ribbon interaction,
/// and localization of enum-based grid cells, using a minimal DI container with a mocked <see cref="IApiClient"/>.
/// </summary>
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
        public IStringLocalizer WithCulture(System.Globalization.CultureInfo culture) => this;
    }

    private sealed class DummyGenericLocalizer<T> : IStringLocalizer<T>
    {
        public LocalizedString this[string name] => new(name, name);
        public LocalizedString this[string name, params object[] arguments] => new(name, name);
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => Array.Empty<LocalizedString>();
        public IStringLocalizer WithCulture(System.Globalization.CultureInfo culture) => this;
    }

    private static (FinanceManager.Web.ViewModels.Accounts.BankAccountListViewModel vm, Mock<IApiClient> apiMock) CreateVm(bool isAuthenticated = true)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentUserService>(new TestCurrentUserService { IsAuthenticated = isAuthenticated });
        // register generic localizer so viewmodels resolving IStringLocalizer<Pages> succeed
        services.AddSingleton(typeof(IStringLocalizer<>), typeof(DummyGenericLocalizer<>));
        var apiMock = new Mock<IApiClient>();
        services.AddSingleton(apiMock.Object);
        var sp = services.BuildServiceProvider();
        var vm = new FinanceManager.Web.ViewModels.Accounts.BankAccountListViewModel(sp);
        return (vm, apiMock);
    }

    private static async Task WaitForStatisticsAsync(FinanceManager.Web.ViewModels.Accounts.BankAccountListViewModel vm)
    {
        for (var i = 0; i < 50; i++)
        {
            if (vm.StatisticsState is not AccountStatisticsLoadState.Loading and not AccountStatisticsLoadState.NotStarted)
            {
                return;
            }

            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    /// <summary>
    /// Verifies that an authenticated user's initialization loads the account page from the API and
    /// populates the grid items with non-empty names.
    /// </summary>
    [Fact]
    public async Task Initialize_LoadsAccounts_WhenAuthenticated()
    {
        var (vm, apiMock) = CreateVm(isAuthenticated: true);
        var accounts = new List<AccountDto>
        {
            new AccountDto(Guid.NewGuid(), "A", AccountType.Giro, "DE00", 10m, Guid.NewGuid(), null, SavingsPlanExpectation.Optional, true),
            new AccountDto(Guid.NewGuid(), "B", AccountType.Savings, null, 20m, Guid.NewGuid(), null, SavingsPlanExpectation.Optional, true)
        };
        apiMock.Setup(a => a.GetAccountsAsync(0, 50, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(accounts);
        apiMock.Setup(a => a.GetAccountStatisticsAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(AccountStatisticsDto.Empty);

        await vm.InitializeAsync();

        Assert.True(vm.Loaded);
        Assert.Equal(2, vm.Items.Count);
        Assert.All(vm.Items, a => Assert.False(string.IsNullOrWhiteSpace(a.Name)));
    }

    /// <summary>
    /// Verifies that an unauthenticated caller never reaches the accounts API: initialization raises
    /// <c>AuthenticationRequired</c> exactly once, leaves the view model unloaded, and skips the API call entirely.
    /// </summary>
    [Fact]
    public async Task Initialize_RequiresAuth_WhenNotAuthenticated()
    {
        var (vm, apiMock) = CreateVm(isAuthenticated: false);
        var authEvents = 0;
        vm.AuthenticationRequired += (_, __) => authEvents++;

        await vm.InitializeAsync();

        Assert.Equal(1, authEvents);
        Assert.False(vm.Loaded);
        apiMock.Verify(a => a.GetAccountsAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Verifies that setting a search term is picked up by the subsequent load (the API is queried once)
    /// and that the ribbon then offers a "ClearSearch" action so the user can reset an active filter.
    /// </summary>
    [Fact]
    public async Task SetFilter_AffectsLoad_AndRibbon()
    {
        var (vm, apiMock) = CreateVm();
        var filterId = Guid.NewGuid();
        apiMock.Setup(a => a.GetAccountsAsync(0, 50, null, "A", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AccountDto>());
        apiMock.Setup(a => a.GetAccountStatisticsAsync("A", It.IsAny<CancellationToken>()))
            .ReturnsAsync(AccountStatisticsDto.Empty);

        vm.SetSearch("A");
        await vm.InitializeAsync();

        apiMock.Verify(a => a.GetAccountsAsync(0, 50, null, "A", It.IsAny<CancellationToken>()), Times.Once);

        var loc = new DummyLocalizer();
        var groups = vm.GetRibbon(loc)!;
        Assert.Contains(groups, g => g.Items.Any(i => i.Action == "ClearSearch"));
    }

    /// <summary>
    /// Verifies that the ribbon always offers a "New" action, so users can create an account from the list view
    /// regardless of current filter or load state.
    /// </summary>
    [Fact]
    public void GetRibbon_ContainsNew()
    {
        var (vm, _) = CreateVm();
        var loc = new DummyLocalizer();
        var groups = vm.GetRibbon(loc)!;
        Assert.Contains(groups, g => g.Items.Any(i => i.Action == "New"));
    }

    /// <summary>
    /// Verifies that grid records render the account type as its localization key (e.g. "EnumType_AccountType_Giro")
    /// rather than the raw enum value, so the UI layer can resolve it through the localizer.
    /// </summary>
    [Fact]
    public async Task Initialize_UsesLocalizedAccountTypeInRecords()
    {
        var (vm, apiMock) = CreateVm(isAuthenticated: true);
        var accounts = new List<AccountDto>
        {
            new AccountDto(Guid.NewGuid(), "A", AccountType.Giro, "DE00", 10m, Guid.NewGuid(), null, SavingsPlanExpectation.Optional, true)
        };
        apiMock.Setup(a => a.GetAccountsAsync(0, 50, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(accounts);
        apiMock.Setup(a => a.GetAccountStatisticsAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(AccountStatisticsDto.Empty);

        await vm.InitializeAsync();

        Assert.Contains(vm.Records, r => r.Cells.Any(c => string.Equals(c.Text, "EnumType_AccountType_Giro", StringComparison.Ordinal)));
    }

    /// <summary>
    /// Ensures list and statistics calls receive the same trimmed search value and clear search removes it for both calls.
    /// </summary>
    [Fact]
    public async Task SearchAndClearReloadListAndStatisticsWithSameQ()
    {
        var (vm, apiMock) = CreateVm();
        apiMock.Setup(a => a.GetAccountsAsync(0, 50, null, "DE123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<AccountDto>());
        apiMock.Setup(a => a.GetAccountsAsync(0, 50, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<AccountDto>());
        apiMock.Setup(a => a.GetAccountStatisticsAsync("DE123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(AccountStatisticsDto.Empty);
        apiMock.Setup(a => a.GetAccountStatisticsAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(AccountStatisticsDto.Empty);

        vm.SetSearch("  DE123  ");
        await vm.InitializeAsync();
        await WaitForStatisticsAsync(vm);
        vm.ClearSearch();
        vm.ResetAndSearch();
        await vm.LoadAsync();
        await WaitForStatisticsAsync(vm);

        apiMock.Verify(a => a.GetAccountsAsync(0, 50, null, "DE123", It.IsAny<CancellationToken>()), Times.Once);
        apiMock.Verify(a => a.GetAccountStatisticsAsync("DE123", It.IsAny<CancellationToken>()), Times.Once);
        apiMock.Verify(a => a.GetAccountsAsync(0, 50, null, null, It.IsAny<CancellationToken>()), Times.Once);
        apiMock.Verify(a => a.GetAccountStatisticsAsync(null, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Confirms a statistics failure does not set the general list error or discard loaded account rows.
    /// </summary>
    [Fact]
    public async Task StatisticsFailureDoesNotSetListError()
    {
        var (vm, apiMock) = CreateVm();
        var accounts = new List<AccountDto>
        {
            new AccountDto(Guid.NewGuid(), "A", AccountType.Giro, "DE00", 10m, Guid.NewGuid(), null, SavingsPlanExpectation.Optional, true)
        };
        apiMock.Setup(a => a.GetAccountsAsync(0, 50, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(accounts);
        apiMock.Setup(a => a.GetAccountStatisticsAsync(null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("statistics down"));

        await vm.InitializeAsync();
        await WaitForStatisticsAsync(vm);

        Assert.Single(vm.Items);
        Assert.Null(vm.LastError);
        Assert.Equal(AccountStatisticsLoadState.Error, vm.StatisticsState);
        Assert.Equal("statistics down", vm.StatisticsErrorMessage);
    }

    /// <summary>
    /// Ensures retry uses the current trimmed search and older overlapping statistics responses cannot overwrite it.
    /// </summary>
    [Fact]
    public async Task RetryUsesCurrentSearchAndStaleResponseIsIgnored()
    {
        var (vm, apiMock) = CreateVm();
        var oldStatistics = new TaskCompletionSource<AccountStatisticsDto>(TaskCreationOptions.RunContinuationsAsynchronously);
        var newStatistics = new TaskCompletionSource<AccountStatisticsDto>(TaskCreationOptions.RunContinuationsAsynchronously);
        var retryStatistics = new AccountStatisticsDto(1, 30m, 0m, 0m, 30m, [], []);
        apiMock.Setup(a => a.GetAccountsAsync(0, 50, null, "old", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<AccountDto>());
        apiMock.Setup(a => a.GetAccountsAsync(0, 50, null, "new", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<AccountDto>());
        apiMock.Setup(a => a.GetAccountsAsync(0, 50, null, "retry", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<AccountDto>());
        apiMock.Setup(a => a.GetAccountStatisticsAsync("old", It.IsAny<CancellationToken>()))
            .Returns(oldStatistics.Task);
        apiMock.Setup(a => a.GetAccountStatisticsAsync("new", It.IsAny<CancellationToken>()))
            .Returns(newStatistics.Task);
        apiMock.Setup(a => a.GetAccountStatisticsAsync("retry", It.IsAny<CancellationToken>()))
            .ReturnsAsync(retryStatistics);

        vm.SetSearch("old");
        await vm.InitializeAsync();
        vm.SetSearch("new");
        vm.ResetAndSearch();
        await vm.LoadAsync();
        newStatistics.SetResult(new AccountStatisticsDto(1, 20m, 0m, 0m, 20m, [], []));
        await WaitForStatisticsAsync(vm);
        oldStatistics.SetResult(new AccountStatisticsDto(1, 10m, 0m, 0m, 10m, [], []));
        await Task.Delay(20, TestContext.Current.CancellationToken);

        Assert.Equal(20m, vm.Statistics?.TotalBalance);

        vm.SetSearch(" retry ");
        await vm.RetryStatisticsAsync();
        await WaitForStatisticsAsync(vm);

        Assert.Equal(30m, vm.Statistics?.TotalBalance);
        apiMock.Verify(a => a.GetAccountStatisticsAsync("retry", It.IsAny<CancellationToken>()), Times.Once);
    }
}
