using FinanceManager.Application;
using FinanceManager.Shared;
using FinanceManager.Web.Services;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace FinanceManager.Tests.ViewModels;

/// <summary>
/// Covers <see cref="SetupProfileViewModel"/>'s user profile settings: loading, the save/dirty-reset
/// cycle, clearing the stored Alpha Vantage API key (and that clearing sends an explicit clear flag rather
/// than an empty string), auto-detected timezone/language application, and that disabling local KPI
/// caching purges the existing cache on save.
/// </summary>
public sealed class SetupProfileViewModelTests
{
    private sealed class TestCurrentUserService : ICurrentUserService
    {
        public Guid UserId { get; set; } = Guid.NewGuid();
        public string? PreferredLanguage { get; set; }
        public bool IsAuthenticated { get; set; } = true;
        public bool IsAdmin { get; set; }
    }

    private static IServiceProvider CreateSp(IApiClient api) => CreateSp(api, Mock.Of<IKpiLocalStorageCache>());

    private static IServiceProvider CreateSp(IApiClient api, IKpiLocalStorageCache kpiCache)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentUserService>(new TestCurrentUserService());
        services.AddSingleton(kpiCache);
        services.AddSingleton(api);
        services.AddSingleton<IConfirmationService>(Mock.Of<IConfirmationService>());
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Verifies that loading populates the profile model, the "has key"/"share key" flags from the API
    /// response, and leaves the view model clean (not dirty) immediately after load.
    /// </summary>
    [Fact]
    public async Task Initialize_Loads_Profile()
    {
        var dto = new UserProfileSettingsDto { PreferredLanguage = "de", TimeZoneId = "Europe/Berlin", HasAlphaVantageApiKey = true, ShareAlphaVantageApiKey = true };
        var apiMock = new Mock<IApiClient>();
        apiMock.Setup(a => a.UserSettings_GetProfileAsync(It.IsAny<CancellationToken>())).ReturnsAsync(dto);

        var vm = new SetupProfileViewModel(CreateSp(apiMock.Object));
        await vm.LoadAsync(TestContext.Current.CancellationToken);

        Assert.False(vm.Loading);
        Assert.Equal("de", vm.Model.PreferredLanguage);
        Assert.True(vm.HasKey);
        Assert.True(vm.ShareKey);
        Assert.False(vm.Dirty);
    }

    /// <summary>
    /// Verifies that editing profile fields (language, API key input, share flag) marks the view model
    /// dirty, and that saving persists the change, sets <c>SavedOk</c>, clears the dirty flag, and resets
    /// the key input field back to empty so the raw key value is not left lingering in the UI after submission.
    /// </summary>
    [Fact]
    public async Task Save_Updates_State_And_Resets_Flags_On_Success()
    {
        var dto = new UserProfileSettingsDto { PreferredLanguage = "de", TimeZoneId = "Europe/Berlin", HasAlphaVantageApiKey = false, ShareAlphaVantageApiKey = false };
        var apiMock = new Mock<IApiClient>();
        apiMock.Setup(a => a.UserSettings_GetProfileAsync(It.IsAny<CancellationToken>())).ReturnsAsync(dto);
        apiMock.Setup(a => a.UserSettings_UpdateProfileAsync(It.IsAny<UserProfileSettingsUpdateRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var vm = new SetupProfileViewModel(CreateSp(apiMock.Object));
        await vm.LoadAsync(TestContext.Current.CancellationToken);

        vm.Model.PreferredLanguage = "en";
        vm.KeyInput = "abc";
        vm.ShareKey = true;
        vm.OnChanged();
        Assert.True(vm.Dirty);

        await vm.SaveAsync(TestContext.Current.CancellationToken);
        apiMock.Verify(a => a.UserSettings_UpdateProfileAsync(It.IsAny<UserProfileSettingsUpdateRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.True(vm.SavedOk);
        Assert.False(vm.Dirty);
        Assert.Equal(string.Empty, vm.KeyInput);
    }

    /// <summary>
    /// Verifies that requesting to clear the stored API key marks the view model dirty, and that saving
    /// sends an explicit <c>ClearAlphaVantageApiKey = true</c> flag in the update request - the mechanism
    /// that distinguishes "remove the stored key" from "leave the key unchanged" (an omitted/empty key
    /// input alone would not be enough to signal deletion).
    /// </summary>
    [Fact]
    public async Task ClearKey_Sets_Dirty_And_Save_Sends_ClearFlag()
    {
        var dto = new UserProfileSettingsDto { PreferredLanguage = "de", TimeZoneId = "Europe/Berlin", HasAlphaVantageApiKey = true, ShareAlphaVantageApiKey = false };
        var apiMock = new Mock<IApiClient>();
        apiMock.Setup(a => a.UserSettings_GetProfileAsync(It.IsAny<CancellationToken>())).ReturnsAsync(dto);
        apiMock.Setup(a => a.UserSettings_UpdateProfileAsync(It.IsAny<UserProfileSettingsUpdateRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var vm = new SetupProfileViewModel(CreateSp(apiMock.Object));
        await vm.LoadAsync(TestContext.Current.CancellationToken);

        vm.ClearKey();
        Assert.True(vm.Dirty);

        await vm.SaveAsync(TestContext.Current.CancellationToken);
        apiMock.Verify(a => a.UserSettings_UpdateProfileAsync(
            It.Is<UserProfileSettingsUpdateRequest>(r => r.ClearAlphaVantageApiKey == true),
            It.IsAny<CancellationToken>()), Times.Once);
        Assert.True(vm.SavedOk);
        Assert.False(vm.Dirty);
    }

    /// <summary>
    /// Verifies that applying an auto-detected language/timezone (e.g. from the browser) updates the
    /// model's corresponding fields and marks the view model dirty so the detected values are actually
    /// persisted on the next save.
    /// </summary>
    [Fact]
    public void SetDetected_Updates_Model_And_Dirty()
    {
        var apiMock = new Mock<IApiClient>();
        var vm = new SetupProfileViewModel(CreateSp(apiMock.Object));
        vm.SetDetectedTimezone("de-DE", "Europe/Berlin");
        Assert.Equal("de-DE", vm.Model.PreferredLanguage);
        Assert.Equal("Europe/Berlin", vm.Model.TimeZoneId);
        Assert.True(vm.Dirty);
    }

    /// <summary>
    /// Verifies that toggling <see cref="UserProfileSettingsDto.ShowConfirmations"/> marks the view model
    /// dirty and that the value is persisted in the update request sent to the API.
    /// </summary>
    [Fact]
    public async Task Save_Persists_ShowConfirmations()
    {
        var dto = new UserProfileSettingsDto
        {
            PreferredLanguage = "de",
            TimeZoneId = "Europe/Berlin",
            ShowConfirmations = true
        };

        var apiMock = new Mock<IApiClient>();
        apiMock.Setup(a => a.UserSettings_GetProfileAsync(It.IsAny<CancellationToken>())).ReturnsAsync(dto);
        apiMock.Setup(a => a.UserSettings_UpdateProfileAsync(It.IsAny<UserProfileSettingsUpdateRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var vm = new SetupProfileViewModel(CreateSp(apiMock.Object));
        await vm.LoadAsync(TestContext.Current.CancellationToken);

        vm.Model.ShowConfirmations = false;
        vm.OnChanged();
        Assert.True(vm.Dirty);

        await vm.SaveAsync(TestContext.Current.CancellationToken);
        apiMock.Verify(a => a.UserSettings_UpdateProfileAsync(
            It.Is<UserProfileSettingsUpdateRequest>(r => r.ShowConfirmations == false),
            It.IsAny<CancellationToken>()), Times.Once);
        Assert.True(vm.SavedOk);
        Assert.False(vm.Dirty);
    }

    /// <summary>
    /// Verifies that turning off local KPI caching and saving purges the existing local KPI cache exactly
    /// once, so stale cached KPI data is not left behind once the user opts out of caching.
    /// </summary>
    [Fact]
    public async Task Save_WithCacheKpisDisabled_ClearsKpiCache()
    {
        var dto = new UserProfileSettingsDto
        {
            PreferredLanguage = "de",
            TimeZoneId = "Europe/Berlin",
            HasAlphaVantageApiKey = true,
            ShareAlphaVantageApiKey = false,
            CacheKpisInLocalStorage = true
        };

        var apiMock = new Mock<IApiClient>();
        apiMock.Setup(a => a.UserSettings_GetProfileAsync(It.IsAny<CancellationToken>())).ReturnsAsync(dto);
        apiMock.Setup(a => a.UserSettings_UpdateProfileAsync(It.IsAny<UserProfileSettingsUpdateRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var kpiCacheMock = new Mock<IKpiLocalStorageCache>();
        var vm = new SetupProfileViewModel(CreateSp(apiMock.Object, kpiCacheMock.Object));
        await vm.LoadAsync(TestContext.Current.CancellationToken);

        vm.Model.CacheKpisInLocalStorage = false;
        vm.OnChanged();

        await vm.SaveAsync(TestContext.Current.CancellationToken);

        Assert.True(vm.SavedOk);
        kpiCacheMock.Verify(k => k.RemoveAllAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
