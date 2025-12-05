using FinanceManager.Application;
using FinanceManager.Shared;
using FinanceManager.Web.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Moq;

namespace FinanceManager.Tests.ViewModels;

public sealed class SavingsPlanEditViewModelTests
{
    private sealed class TestCurrentUserService : ICurrentUserService
    {
        public Guid UserId { get; set; } = Guid.NewGuid();
        public string? PreferredLanguage { get; set; }
        public bool IsAuthenticated { get; set; } = true;
        public bool IsAdmin { get; set; }
    }

    private sealed class TestLocalizer<T> : IStringLocalizer<T>
    {
        public LocalizedString this[string name] => new(name, name);
        public LocalizedString this[string name, params object[] arguments] => new(name, string.Format(name, arguments));
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) { yield break; }
    }

    private static (SavingsPlanEditViewModel vm, Mock<IApiClient> apiMock) CreateVm()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentUserService>(new TestCurrentUserService());
        var apiMock = new Mock<IApiClient>();
        services.AddSingleton(apiMock.Object);
        var sp = services.BuildServiceProvider();
        var vm = new SavingsPlanEditViewModel(sp);
        return (vm, apiMock);
    }

    [Fact]
    public async Task InitializeAsync_Loads_Edit()
    {
        var (vm, apiMock) = CreateVm();
        var id = Guid.NewGuid();
        var plan = new SavingsPlanDto(id, "Plan A", SavingsPlanType.Recurring, 1000m, new DateTime(2026, 1, 1), SavingsPlanInterval.Monthly, true, DateTime.UtcNow, null, null);
        var analysis = new SavingsPlanAnalysisDto(id, true, 1000m, new DateTime(2026, 1, 1), 200m, 50m, 20);
        var cats = new List<SavingsPlanCategoryDto> { new SavingsPlanCategoryDto { Id = Guid.NewGuid(), Name = "Cat1" } };

        apiMock.Setup(a => a.SavingsPlans_GetAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(plan);
        apiMock.Setup(a => a.SavingsPlans_AnalyzeAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(analysis);
        apiMock.Setup(a => a.SavingsPlanCategories_ListAsync(It.IsAny<CancellationToken>())).ReturnsAsync(cats);

        await vm.InitializeAsync(id, backNav: null, draftId: null, entryId: null, prefillName: null);

        Assert.True(vm.IsEdit);
        Assert.True(vm.Loaded);
        Assert.Equal("Plan A", vm.Model.Name);
        Assert.NotNull(vm.Analysis);
        Assert.Single(vm.Categories);
    }

    [Fact]
    public async Task InitializeAsync_New_Prefill_Sets_Name()
    {
        var (vm, apiMock) = CreateVm();
        apiMock.Setup(a => a.SavingsPlanCategories_ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SavingsPlanCategoryDto>());

        await vm.InitializeAsync(null, backNav: null, draftId: null, entryId: null, prefillName: "Hello");

        Assert.False(vm.IsEdit);
        Assert.Equal("Hello", vm.Model.Name);
        Assert.True(vm.Loaded);
    }

    [Fact]
    public async Task SaveAsync_Edit_Success()
    {
        var (vm, apiMock) = CreateVm();
        var id = Guid.NewGuid();
        var plan = new SavingsPlanDto(id, "Plan A", SavingsPlanType.Recurring, null, null, null, true, DateTime.UtcNow, null, null);

        apiMock.Setup(a => a.SavingsPlans_GetAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(plan);
        apiMock.Setup(a => a.SavingsPlanCategories_ListAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<SavingsPlanCategoryDto>());
        apiMock.Setup(a => a.SavingsPlans_UpdateAsync(id, It.IsAny<SavingsPlanCreateRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(plan);

        await vm.InitializeAsync(id, null, null, null, null);
        vm.Model.Name = "Updated";
        var res = await vm.SaveAsync();

        Assert.NotNull(res);
        Assert.Null(vm.Error);
    }

    [Fact]
    public async Task SaveAsync_New_Success()
    {
        var (vm, apiMock) = CreateVm();
        var id = Guid.NewGuid();
        var plan = new SavingsPlanDto(id, "Created", SavingsPlanType.OneTime, null, null, null, true, DateTime.UtcNow, null, null);

        apiMock.Setup(a => a.SavingsPlanCategories_ListAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<SavingsPlanCategoryDto>());
        apiMock.Setup(a => a.SavingsPlans_CreateAsync(It.IsAny<SavingsPlanCreateRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(plan);

        await vm.InitializeAsync(null, null, null, null, null);
        vm.Model.Name = "Created";
        var res = await vm.SaveAsync();

        Assert.NotNull(res);
        Assert.Equal(id, res!.Id);
    }

    [Fact]
    public async Task Archive_Delete_Set_Error_On_Fail()
    {
        var (vm, apiMock) = CreateVm();
        var id = Guid.NewGuid();
        var plan = new SavingsPlanDto(id, "Plan A", SavingsPlanType.OneTime, null, null, null, true, DateTime.UtcNow, null, null);

        apiMock.Setup(a => a.SavingsPlans_GetAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(plan);
        apiMock.Setup(a => a.SavingsPlanCategories_ListAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<SavingsPlanCategoryDto>());
        apiMock.Setup(a => a.SavingsPlans_ArchiveAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        apiMock.Setup(a => a.SavingsPlans_DeleteAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        apiMock.SetupGet(a => a.LastError).Returns("bad");

        await vm.InitializeAsync(id, null, null, null, null);

        var ok1 = await vm.ArchiveAsync();
        Assert.False(ok1);
        Assert.NotNull(vm.Error);

        var ok2 = await vm.DeleteAsync();
        Assert.False(ok2);
        Assert.NotNull(vm.Error);
    }

    [Fact]
    public void Ribbon_Disables_Save_If_Name_Short()
    {
        var (vm, _) = CreateVm();
        var loc = new TestLocalizer<SavingsPlanEditViewModelTests>();
        var groups = vm.GetRibbon(loc);
        var editGroup = groups.First(g => g.Title == "Ribbon_Group_Edit");
        var save = editGroup.Items.First(i => i.Action == "Save");
        var archive = editGroup.Items.First(i => i.Action == "Archive");
        Assert.True(save.Disabled);
        Assert.True(archive.Disabled);

        vm.Model.Name = "OK";
        groups = vm.GetRibbon(loc);
        save = groups.First(g => g.Title == "Ribbon_Group_Edit").Items.First(i => i.Action == "Save");
        Assert.False(save.Disabled);
    }

    [Fact]
    public async Task InitializeAsync_Loads_Analysis_For_OpenPlan_With_Postings()
    {
        var (vm, apiMock) = CreateVm();
        var id = Guid.NewGuid();
        var catId = Guid.NewGuid();
        var plan = new SavingsPlanDto(id, "Plan Open", SavingsPlanType.Open, 0m, null, null, true, DateTime.UtcNow, null, catId, null);
        var analysis = new SavingsPlanAnalysisDto(id, true, 0m, null, 150m, 0m, 0);
        var cats = new List<SavingsPlanCategoryDto> { new SavingsPlanCategoryDto { Id = catId, Name = "Sparen" } };

        apiMock.Setup(a => a.SavingsPlans_GetAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(plan);
        apiMock.Setup(a => a.SavingsPlans_AnalyzeAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(analysis);
        apiMock.Setup(a => a.SavingsPlanCategories_ListAsync(It.IsAny<CancellationToken>())).ReturnsAsync(cats);

        await vm.InitializeAsync(id, backNav: null, draftId: null, entryId: null, prefillName: null);

        Assert.True(vm.IsEdit);
        Assert.True(vm.Loaded);
        Assert.NotNull(vm.Analysis);
        Assert.Equal(150m, vm.Analysis!.AccumulatedAmount);
        Assert.Equal(0m, vm.Analysis!.TargetAmount);
        Assert.Null(vm.Analysis!.TargetDate);
        Assert.True(vm.Analysis!.TargetReachable);
        Assert.Single(vm.Categories);
        Assert.Equal(catId, vm.Model.CategoryId);
    }
}
