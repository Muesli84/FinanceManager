using FluentAssertions;
using FinanceManager.Infrastructure;
using FinanceManager.Web.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FinanceManager.Tests.Integration.ApiClient;

/// <summary>
/// End-to-end test for the per-user settings API: profile defaults and updates (language, timezone,
/// protected Alpha Vantage API key storage), notification preferences, and CSV import-splitting
/// preferences.
/// </summary>
public class ApiClientUserSettingsTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    /// <summary>
    /// Initializes a new instance of the <see cref="ApiClientUserSettingsTests"/> class.
    /// </summary>
    /// <param name="factory">Shared web application factory providing the in-memory test server.</param>
    public ApiClientUserSettingsTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private FinanceManager.Shared.ApiClient CreateClient()
    {
        var http = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });
        return new FinanceManager.Shared.ApiClient(http);
    }

    private async Task<string> EnsureAuthenticatedAsync(FinanceManager.Shared.ApiClient api)
    {
        var username = $"user_{Guid.NewGuid():N}";
        await api.Auth_RegisterAsync(new RegisterRequest(username, "Secret123", PreferredLanguage: null, TimeZoneId: null));
        return username;
    }

    /// <summary>
    /// Verifies that a freshly registered user's profile starts with no language/timezone preference, no
    /// stored Alpha Vantage API key, KPI caching disabled, and confirmation dialogs enabled.
    /// </summary>
    [Fact]
    public async Task UserSettings_GetProfile_Returns_Defaults()
    {
        var api = CreateClient();
        await EnsureAuthenticatedAsync(api);

        var profile = await api.UserSettings_GetProfileAsync(TestContext.Current.CancellationToken);
        profile.Should().NotBeNull();
        // defaults: no language, no timezone, no API key, KPI caching disabled, confirmations enabled
        profile!.HasAlphaVantageApiKey.Should().BeFalse();
        profile.ShareAlphaVantageApiKey.Should().BeFalse();
        profile.CacheKpisInLocalStorage.Should().BeFalse();
        profile.ShowConfirmations.Should().BeTrue();
    }

    /// <summary>
    /// Verifies that toggling the "show confirmation dialogs" preference persists and is reflected back on
    /// the next read.
    /// </summary>
    [Fact]
    public async Task UserSettings_UpdateProfile_Persists_ShowConfirmations()
    {
        var api = CreateClient();
        await EnsureAuthenticatedAsync(api);

        var ok = await api.UserSettings_UpdateProfileAsync(new UserProfileSettingsUpdateRequest(
            PreferredLanguage: null,
            TimeZoneId: null,
            AlphaVantageApiKey: null,
            ClearAlphaVantageApiKey: null,
            ShareAlphaVantageApiKey: null,
            CacheKpisInLocalStorage: false,
            ShowConfirmations: false), TestContext.Current.CancellationToken);
        ok.Should().BeTrue();

        var profile = await api.UserSettings_GetProfileAsync(TestContext.Current.CancellationToken);
        profile.Should().NotBeNull();
        profile!.ShowConfirmations.Should().BeFalse();
    }

    /// <summary>
    /// Verifies that updating the profile's preferred language and timezone persists and is reflected
    /// back on the next read.
    /// </summary>
    [Fact]
    public async Task UserSettings_UpdateProfile_Sets_Language_And_Timezone()
    {
        var api = CreateClient();
        await EnsureAuthenticatedAsync(api);

        var ok = await api.UserSettings_UpdateProfileAsync(new UserProfileSettingsUpdateRequest(
            PreferredLanguage: "de",
            TimeZoneId: "Europe/Berlin",
            AlphaVantageApiKey: null,
            ClearAlphaVantageApiKey: null,
            ShareAlphaVantageApiKey: null,
            CacheKpisInLocalStorage: false), TestContext.Current.CancellationToken);
        ok.Should().BeTrue();

        var profile = await api.UserSettings_GetProfileAsync(TestContext.Current.CancellationToken);
        profile.Should().NotBeNull();
        profile!.PreferredLanguage.Should().Be("de");
        profile.TimeZoneId.Should().Be("Europe/Berlin");
    }

    /// <summary>
    /// Verifies that toggling the "cache KPIs in local storage" preference persists across a subsequent
    /// profile read.
    /// </summary>
    [Fact]
    public async Task UserSettings_UpdateProfile_Persists_CacheKpis()
    {
        var api = CreateClient();
        await EnsureAuthenticatedAsync(api);

        var ok = await api.UserSettings_UpdateProfileAsync(new UserProfileSettingsUpdateRequest(
            PreferredLanguage: null,
            TimeZoneId: null,
            AlphaVantageApiKey: null,
            ClearAlphaVantageApiKey: null,
            ShareAlphaVantageApiKey: null,
            CacheKpisInLocalStorage: true), TestContext.Current.CancellationToken);
        ok.Should().BeTrue();

        var profile = await api.UserSettings_GetProfileAsync(TestContext.Current.CancellationToken);
        profile.Should().NotBeNull();
        profile!.CacheKpisInLocalStorage.Should().BeTrue();
    }

    /// <summary>
    /// Verifies that a submitted Alpha Vantage API key is never stored in plaintext: the persisted value
    /// in the database must be protected (prefixed accordingly) and only decrypt back to the original
    /// plaintext via the registered <see cref="IAlphaVantageSecretProtector"/> - guards against accidental
    /// storage of a sensitive third-party API key in the clear.
    /// </summary>
    [Fact]
    public async Task UserSettings_UpdateProfile_Stores_Protected_AlphaVantageApiKey()
    {
        var api = CreateClient();
        var username = await EnsureAuthenticatedAsync(api);
        const string plaintext = "ALPHAVANTAGE-SECRET";

        var ok = await api.UserSettings_UpdateProfileAsync(new UserProfileSettingsUpdateRequest(
            PreferredLanguage: null,
            TimeZoneId: null,
            AlphaVantageApiKey: plaintext,
            ClearAlphaVantageApiKey: null,
            ShareAlphaVantageApiKey: null,
            CacheKpisInLocalStorage: false), TestContext.Current.CancellationToken);

        ok.Should().BeTrue();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var protector = scope.ServiceProvider.GetRequiredService<IAlphaVantageSecretProtector>();
        var stored = await db.Users
            .Where(u => u.UserName == username)
            .Select(u => u.AlphaVantageApiKey)
            .SingleAsync(cancellationToken: TestContext.Current.CancellationToken);
        stored.Should().NotBeNull();
        stored.Should().NotBe(plaintext);
        stored.Should().StartWith(DataProtectionAlphaVantageSecretProtector.ProtectedPrefix);
        protector.Unprotect(stored).Should().Be(plaintext);

        var profile = await api.UserSettings_GetProfileAsync(TestContext.Current.CancellationToken);
        profile!.HasAlphaVantageApiKey.Should().BeTrue();
    }

    /// <summary>
    /// Verifies that explicitly clearing the Alpha Vantage API key removes the stored (protected) value
    /// entirely rather than leaving a stale encrypted remnant behind.
    /// </summary>
    [Fact]
    public async Task UserSettings_UpdateProfile_ClearAlphaVantageApiKey_RemovesStoredValue()
    {
        var api = CreateClient();
        var username = await EnsureAuthenticatedAsync(api);
        await api.UserSettings_UpdateProfileAsync(new UserProfileSettingsUpdateRequest(
            PreferredLanguage: null,
            TimeZoneId: null,
            AlphaVantageApiKey: "ALPHAVANTAGE-SECRET",
            ClearAlphaVantageApiKey: null,
            ShareAlphaVantageApiKey: null,
            CacheKpisInLocalStorage: false), TestContext.Current.CancellationToken);

        var ok = await api.UserSettings_UpdateProfileAsync(new UserProfileSettingsUpdateRequest(
            PreferredLanguage: null,
            TimeZoneId: null,
            AlphaVantageApiKey: null,
            ClearAlphaVantageApiKey: true,
            ShareAlphaVantageApiKey: null,
            CacheKpisInLocalStorage: false), TestContext.Current.CancellationToken);

        ok.Should().BeTrue();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.Users
            .Where(u => u.UserName == username)
            .Select(u => u.AlphaVantageApiKey)
            .SingleAsync(cancellationToken: TestContext.Current.CancellationToken);
        stored.Should().BeNull();
    }

    /// <summary>
    /// Verifies that a new user's notification settings start with the monthly reminder disabled.
    /// </summary>
    [Fact]
    public async Task UserSettings_GetNotifications_Returns_Defaults()
    {
        var api = CreateClient();
        await EnsureAuthenticatedAsync(api);

        var notifications = await api.User_GetNotificationSettingsAsync(TestContext.Current.CancellationToken);
        notifications.Should().NotBeNull();
        notifications!.MonthlyReminderEnabled.Should().BeFalse();
    }

    /// <summary>
    /// Verifies that enabling the monthly reminder with a specific time and provider persists and is
    /// reflected back on the next read.
    /// </summary>
    [Fact]
    public async Task UserSettings_UpdateNotifications_Works()
    {
        var api = CreateClient();
        await EnsureAuthenticatedAsync(api);

        var ok = await api.User_UpdateNotificationSettingsAsync(monthlyEnabled: true, hour: 10, minute: 30, provider: "Memory", country: null, subdivision: null, ct: TestContext.Current.CancellationToken);
        ok.Should().BeTrue();

        var notifications = await api.User_GetNotificationSettingsAsync(TestContext.Current.CancellationToken);
        notifications.Should().NotBeNull();
        notifications!.MonthlyReminderEnabled.Should().BeTrue();
        notifications.MonthlyReminderHour.Should().Be(10);
        notifications.MonthlyReminderMinute.Should().Be(30);
    }

    /// <summary>
    /// Verifies the default CSV import-splitting configuration a new user starts with: monthly-or-fixed
    /// splitting mode, a 250-entry cap per draft, and confirmation only when information is missing.
    /// </summary>
    [Fact]
    public async Task UserSettings_GetImportSplit_Returns_Defaults()
    {
        var api = CreateClient();
        await EnsureAuthenticatedAsync(api);

        var split = await api.UserSettings_GetImportSplitAsync(TestContext.Current.CancellationToken);
        split.Should().NotBeNull();
        split!.Mode.Should().Be(ImportSplitMode.MonthlyOrFixed);
        split.MaxEntriesPerDraft.Should().Be(250);
        split.MassImportDialogPolicy.Should().Be(MassImportDialogPolicy.OnMissingInformation);
    }

    /// <summary>
    /// Verifies that switching the import-splitting mode to a fixed size with custom min/max entry counts
    /// and a stricter confirmation policy persists and is reflected back on the next read.
    /// </summary>
    [Fact]
    public async Task UserSettings_UpdateImportSplit_Works()
    {
        var api = CreateClient();
        await EnsureAuthenticatedAsync(api);

        var ok = await api.UserSettings_UpdateImportSplitAsync(new ImportSplitSettingsUpdateRequest(
            Mode: ImportSplitMode.FixedSize,
            MaxEntriesPerDraft: 100,
            MonthlySplitThreshold: null,
            MinEntriesPerDraft: 5,
            MassImportDialogPolicy: MassImportDialogPolicy.AlwaysConfirm), TestContext.Current.CancellationToken);
        ok.Should().BeTrue();

        var split = await api.UserSettings_GetImportSplitAsync(TestContext.Current.CancellationToken);
        split.Should().NotBeNull();
        split!.Mode.Should().Be(ImportSplitMode.FixedSize);
        split.MaxEntriesPerDraft.Should().Be(100);
        split.MinEntriesPerDraft.Should().Be(5);
        split.MassImportDialogPolicy.Should().Be(MassImportDialogPolicy.AlwaysConfirm);
    }
}
