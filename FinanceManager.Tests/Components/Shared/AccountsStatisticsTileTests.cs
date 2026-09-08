using Bunit;
using FinanceManager.Application;
using FinanceManager.Shared;
using FinanceManager.Web.Components.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Moq;

namespace FinanceManager.Tests.Components.Shared;

/// <summary>
/// Component tests for the bank account statistics tile.
/// </summary>
public sealed class AccountsStatisticsTileTests : BunitContext
{
    private sealed class TestCurrentUserService : ICurrentUserService
    {
        public Guid UserId => Guid.NewGuid();
        public string? PreferredLanguage => "en";
        public bool IsAuthenticated => true;
        public bool IsAdmin => false;
    }

    private sealed class DummyGenericLocalizer<T> : IStringLocalizer<T>
    {
        public LocalizedString this[string name] => new(name, name);
        public LocalizedString this[string name, params object[] arguments] => new(name, name);
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => Array.Empty<LocalizedString>();
        public IStringLocalizer WithCulture(System.Globalization.CultureInfo culture) => this;
    }

    /// <summary>
    /// Loading renders a busy tile with stable KPI and chart placeholders and without old values.
    /// </summary>
    [Fact]
    public void Loading_RendersStableBusySkeleton()
    {
        var vm = CreateVm(Mock.Of<IApiClient>());
        Set(vm, nameof(BankAccountListViewModel.StatisticsState), AccountStatisticsLoadState.Loading);

        var cut = Render<AccountsStatisticsTile>(builder => builder.Add(c => c.ViewModel, vm));

        Assert.Equal("true", cut.Find(".accounts-statistics").GetAttribute("aria-busy"));
        Assert.Equal("Loading", cut.Find(".accounts-statistics").GetAttribute("data-statistics-state"));
        Assert.Equal(3, cut.FindAll("[data-statistics-kpi]").Count);
        Assert.Equal(2, cut.FindAll(".accounts-statistics-chart.loading").Count);
        Assert.DoesNotContain("123", cut.Markup);
    }

    /// <summary>
    /// Empty statistics render zero KPIs, empty chart text and no synthetic donut segments.
    /// </summary>
    [Fact]
    public void Empty_RendersZeroKpisAndNoSlices()
    {
        var vm = CreateVm(Mock.Of<IApiClient>());
        Set(vm, nameof(BankAccountListViewModel.Statistics), AccountStatisticsDto.Empty);
        Set(vm, nameof(BankAccountListViewModel.StatisticsState), AccountStatisticsLoadState.Empty);

        var cut = Render<AccountsStatisticsTile>(builder => builder.Add(c => c.ViewModel, vm));

        Assert.Equal("Empty", cut.Find(".accounts-statistics").GetAttribute("data-statistics-state"));
        Assert.Equal(3, cut.FindAll("[data-statistics-kpi]").Count);
        Assert.Equal(2, cut.FindAll(".accounts-statistics-empty").Count);
        Assert.Empty(cut.FindAll(".donut-segment"));
    }

    /// <summary>
    /// All-zero account groups stay visible in legends but do not render ring segments.
    /// </summary>
    [Fact]
    public void AllZero_RendersGroupsWithoutRingSegments()
    {
        var vm = CreateVm(Mock.Of<IApiClient>());
        Set(vm, nameof(BankAccountListViewModel.Statistics), new AccountStatisticsDto(
            AccountCount: 2,
            TotalBalance: 0m,
            YearToDateChange: 0m,
            MonthToDateChange: 0m,
            TotalGrossMagnitude: 0m,
            ByAccountType:
            [
                new AccountBalanceGroupDto(AccountType.Giro.ToString(), null, 0m, 0m, 0m, 0m, 2, 2)
            ],
            ByBankContact:
            [
                new AccountBalanceGroupDto("bank", "Zero Bank", 0m, 0m, 0m, 0m, 2, 2)
            ]));
        Set(vm, nameof(BankAccountListViewModel.StatisticsState), AccountStatisticsLoadState.Loaded);

        var cut = Render<AccountsStatisticsTile>(builder => builder.Add(c => c.ViewModel, vm));

        Assert.Equal(2, cut.FindAll(".donut-empty-text").Count);
        Assert.Empty(cut.FindAll(".donut-segment"));
        Assert.Contains("AccountsStatistics_ZeroBalances: 2", cut.Markup);
    }

    /// <summary>
    /// Error state renders an alert and wires the retry button to the current search value.
    /// </summary>
    [Fact]
    public void Error_RendersAlertAndRetry()
    {
        var apiMock = new Mock<IApiClient>();
        apiMock.Setup(a => a.GetAccountStatisticsAsync("needle", It.IsAny<CancellationToken>()))
            .ReturnsAsync(AccountStatisticsDto.Empty);
        var vm = CreateVm(apiMock.Object);
        vm.SetSearch(" needle ");
        Set(vm, nameof(BankAccountListViewModel.StatisticsState), AccountStatisticsLoadState.Error);
        Set(vm, nameof(BankAccountListViewModel.StatisticsErrorMessage), "statistics failed");

        var cut = Render<AccountsStatisticsTile>(builder => builder.Add(c => c.ViewModel, vm));

        Assert.Equal("alert", cut.Find(".accounts-statistics-error").GetAttribute("role"));
        Assert.Contains("statistics failed", cut.Markup);
        cut.Find("button").Click();
        apiMock.Verify(a => a.GetAccountStatisticsAsync("needle", It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Unknown and long bank contact labels are rendered completely in wrappable legend markup.
    /// </summary>
    [Fact]
    public void UnknownAndLongBankContactLabel_IsCompleteAndWrappable()
    {
        const string longName = "Very Long Bank Contact Label With Several Readable Segments And No Truncation";
        var vm = CreateVm(Mock.Of<IApiClient>());
        Set(vm, nameof(BankAccountListViewModel.Statistics), new AccountStatisticsDto(
            AccountCount: 2,
            TotalBalance: 3m,
            YearToDateChange: 0m,
            MonthToDateChange: 0m,
            TotalGrossMagnitude: 3m,
            ByAccountType:
            [
                new AccountBalanceGroupDto(AccountType.Giro.ToString(), null, 3m, 3m, 0m, 3m, 2, 0)
            ],
            ByBankContact:
            [
                new AccountBalanceGroupDto("unknown", null, 1m, 1m, 0m, 1m, 1, 0),
                new AccountBalanceGroupDto("known", longName, 2m, 2m, 0m, 2m, 1, 0)
            ]));
        Set(vm, nameof(BankAccountListViewModel.StatisticsState), AccountStatisticsLoadState.Loaded);

        var cut = Render<AccountsStatisticsTile>(builder => builder.Add(c => c.ViewModel, vm));

        Assert.Contains("AccountsStatistics_UnknownBankContact", cut.Markup);
        Assert.Contains(longName, cut.Markup);
        Assert.NotEmpty(cut.FindAll(".donut-legend-label"));
    }

    /// <summary>
    /// Mixed signed balances are rendered losslessly in the legend while gross magnitude drives chart geometry.
    /// </summary>
    [Fact]
    public void MixedBalances_ShowsSignedAmountsAndGrossPercentBasis()
    {
        var vm = CreateVm(Mock.Of<IApiClient>());
        var statistics = new AccountStatisticsDto(
            AccountCount: 2,
            TotalBalance: 0m,
            YearToDateChange: 25m,
            MonthToDateChange: -5m,
            TotalGrossMagnitude: 200m,
            ByAccountType:
            [
                new AccountBalanceGroupDto(AccountType.Giro.ToString(), null, 0m, 100m, 100m, 200m, 2, 0)
            ],
            ByBankContact:
            [
                new AccountBalanceGroupDto("unknown", null, 0m, 100m, 100m, 200m, 2, 0)
            ]);
        Set(vm, nameof(BankAccountListViewModel.Statistics), statistics);
        Set(vm, nameof(BankAccountListViewModel.StatisticsState), AccountStatisticsLoadState.Loaded);

        var cut = Render<AccountsStatisticsTile>(builder => builder.Add(c => c.ViewModel, vm));

        Assert.Equal("Loaded", cut.Find(".accounts-statistics").GetAttribute("data-statistics-state"));
        Assert.NotEmpty(cut.FindAll("[data-statistics-kpi]"));
        Assert.Single(cut.FindAll("[data-statistics-group-key=\"unknown\"]"));
        Assert.Contains("AccountsStatistics_PositiveBalance", cut.Markup);
        Assert.Contains("AccountsStatistics_NegativeBalance", cut.Markup);
        Assert.Contains("AccountsStatistics_GrossMetric", cut.Markup);
    }

    private BankAccountListViewModel CreateVm(IApiClient apiClient)
    {
        Services.AddSingleton<ICurrentUserService>(new TestCurrentUserService());
        Services.AddSingleton(apiClient);
        Services.AddSingleton(typeof(IStringLocalizer<>), typeof(DummyGenericLocalizer<>));
        return new BankAccountListViewModel(Services.BuildServiceProvider());
    }

    private static void Set<T>(BankAccountListViewModel vm, string propertyName, T value)
    {
        typeof(BankAccountListViewModel).GetProperty(propertyName)!.SetValue(vm, value);
    }
}
