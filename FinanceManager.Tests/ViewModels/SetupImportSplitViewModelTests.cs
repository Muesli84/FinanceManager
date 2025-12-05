using FinanceManager.Application;
using FinanceManager.Shared;
using FinanceManager.Web.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace FinanceManager.Tests.ViewModels;

public sealed class SetupImportSplitViewModelTests
{
    private sealed class TestCurrentUserService : ICurrentUserService
    {
        public Guid UserId { get; set; } = Guid.NewGuid();
        public string? PreferredLanguage { get; set; }
        public bool IsAuthenticated { get; set; } = true;
        public bool IsAdmin { get; set; }
    }

    private static IServiceProvider CreateSp(IApiClient api)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentUserService>(new TestCurrentUserService());
        services.AddSingleton(api);
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Initialize_Loads_Settings()
    {
        var dto = new ImportSplitSettingsDto { Mode = ImportSplitMode.Monthly, MaxEntriesPerDraft = 200, MonthlySplitThreshold = 250, MinEntriesPerDraft = 5 };
        var apiMock = new Mock<IApiClient>();
        apiMock.Setup(a => a.UserSettings_GetImportSplitAsync(It.IsAny<CancellationToken>())).ReturnsAsync(dto);

        var vm = new SetupImportSplitViewModel(CreateSp(apiMock.Object));
        await vm.InitializeAsync();

        Assert.False(vm.Loading);
        Assert.NotNull(vm.Model);
        Assert.Equal(ImportSplitMode.Monthly, vm.Model!.Mode);
    }

    [Fact]
    public async Task Validate_Disallows_Invalid_Combinations()
    {
        var apiMock = new Mock<IApiClient>();
        apiMock.Setup(a => a.UserSettings_GetImportSplitAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new ImportSplitSettingsDto());

        var vm = new SetupImportSplitViewModel(CreateSp(apiMock.Object));
        await vm.InitializeAsync();

        vm.Model!.MaxEntriesPerDraft = 10;
        vm.Validate();
        Assert.True(vm.HasValidationError);

        vm.Model!.MaxEntriesPerDraft = 100;
        vm.Model!.Mode = ImportSplitMode.Monthly;
        vm.Model!.MinEntriesPerDraft = 0;
        vm.Validate();
        Assert.True(vm.HasValidationError);

        vm.Model!.MinEntriesPerDraft = 10;
        vm.Model!.MonthlySplitThreshold = 100; // equal to max => ok
        vm.Model!.Mode = ImportSplitMode.MonthlyOrFixed;
        vm.Validate();
        Assert.False(vm.HasValidationError);
        vm.Model!.MonthlySplitThreshold = 50; // less than max => error
        vm.Validate();
        Assert.True(vm.HasValidationError);
    }

    [Fact]
    public async Task Save_Sets_SavedOk_And_Resets_Dirty()
    {
        var dto = new ImportSplitSettingsDto();
        var apiMock = new Mock<IApiClient>();
        apiMock.Setup(a => a.UserSettings_GetImportSplitAsync(It.IsAny<CancellationToken>())).ReturnsAsync(dto);
        apiMock.Setup(a => a.UserSettings_UpdateImportSplitAsync(It.IsAny<ImportSplitSettingsUpdateRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var vm = new SetupImportSplitViewModel(CreateSp(apiMock.Object));
        await vm.InitializeAsync();

        vm.Model!.MaxEntriesPerDraft = 300;
        vm.OnModeChanged();
        Assert.True(vm.Dirty);

        await vm.SaveAsync();
        apiMock.Verify(a => a.UserSettings_UpdateImportSplitAsync(It.IsAny<ImportSplitSettingsUpdateRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.True(vm.SavedOk);
        Assert.False(vm.Dirty);
    }
}
