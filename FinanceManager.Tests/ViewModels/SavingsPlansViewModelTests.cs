using FinanceManager.Application;
using FinanceManager.Shared;
using FinanceManager.Web.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Moq;

namespace FinanceManager.Tests.ViewModels;

public sealed class SavingsPlansViewModelTests
{
    private sealed class TestCurrentUserService : ICurrentUserService
    {
        public Guid UserId { get; set; } = Guid.NewGuid();
        public string? PreferredLanguage { get; set; }
        public bool IsAuthenticated { get; set; } = true;
        public bool IsAdmin { get; set; }
    }

    private static IServiceProvider CreateSp(Mock<IApiClient> apiMock, bool authenticated = true)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentUserService>(new TestCurrentUserService { IsAuthenticated = authenticated });
        services.AddSingleton(apiMock.Object);
        services.AddSingleton(typeof(IStringLocalizer<>), typeof(TestLocalizer<>));
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task InitializeAsync_LoadsPlans_AndAnalyses()
    {
        var plans = new List<SavingsPlanDto>
        {
            new SavingsPlanDto(Guid.NewGuid(), "P1", SavingsPlanType.Recurring, 1000m, new DateTime(2025,1,1), SavingsPlanInterval.Monthly, true, DateTime.UtcNow, null, null, null, null),
            new SavingsPlanDto(Guid.NewGuid(), "P2", SavingsPlanType.Open, null, null, null, true, DateTime.UtcNow, null, null, null, null)
        };

        var apiMock = new Mock<IApiClient>();
        apiMock.Setup(a => a.SavingsPlans_ListAsync(true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(plans);
        apiMock.Setup(a => a.SavingsPlanCategories_ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SavingsPlanCategoryDto>());
        apiMock.Setup(a => a.SavingsPlans_AnalyzeAsync(plans[0].Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SavingsPlanAnalysisDto(plans[0].Id, true, 1000m, new DateTime(2025, 1, 1), 300m, 50m, 14));
        apiMock.Setup(a => a.SavingsPlans_AnalyzeAsync(plans[1].Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SavingsPlanAnalysisDto(plans[1].Id, false, null, null, 0m, 0m, 0));

        var vm = new SavingsPlansViewModel(CreateSp(apiMock));
        await vm.InitializeAsync();

        Assert.True(vm.Loaded);
        Assert.Equal(2, vm.Plans.Count);
    }

    [Fact]
    public async Task ToggleActiveOnly_Reloads()
    {
        int calls = 0;
        var apiMock = new Mock<IApiClient>();
        apiMock.Setup(a => a.SavingsPlans_ListAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => { calls++; return new List<SavingsPlanDto>(); });
        apiMock.Setup(a => a.SavingsPlanCategories_ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SavingsPlanCategoryDto>());

        var vm = new SavingsPlansViewModel(CreateSp(apiMock));

        await vm.InitializeAsync();
        Assert.Equal(1, calls);

        vm.ToggleActiveOnly();
        await Task.Delay(50);
        Assert.True(calls >= 2);
    }

    [Fact]
    public void GetStatusFlags_And_Label_Work()
    {
        var apiMock = new Mock<IApiClient>();
        var sp = CreateSp(apiMock);
        var vm = new SavingsPlansViewModel(sp);

        var planId = Guid.NewGuid();
        var plan = new SavingsPlanDto(planId, "P", SavingsPlanType.Recurring, 1000m, new DateTime(2025, 1, 1), SavingsPlanInterval.Monthly, true, DateTime.UtcNow, null, null, null, null);
        var loc = sp.GetRequiredService<IStringLocalizer<SavingsPlansViewModelTests>>();
        var label = vm.GetStatusLabel(loc, plan);
        Assert.False(string.IsNullOrWhiteSpace(label));
        var flags = vm.GetStatusFlags(plan);
        Assert.False(flags.Reachable);
        Assert.False(flags.Unreachable);
    }

    private sealed class TestLocalizer<T> : IStringLocalizer<T>
    {
        public LocalizedString this[string name] => new LocalizedString(name, name, resourceNotFound: false);
        public LocalizedString this[string name, params object[] arguments] => new LocalizedString(name, string.Format(name, arguments), resourceNotFound: false);
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) { yield break; }
    }
}
