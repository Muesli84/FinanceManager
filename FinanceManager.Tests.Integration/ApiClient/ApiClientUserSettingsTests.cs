using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace FinanceManager.Tests.Integration.ApiClient;

public class ApiClientUserSettingsTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

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

    private async Task EnsureAuthenticatedAsync(FinanceManager.Shared.ApiClient api)
    {
        var username = $"user_{Guid.NewGuid():N}";
        await api.Auth_RegisterAsync(new RegisterRequest(username, "Secret123", PreferredLanguage: null, TimeZoneId: null));
    }

    [Fact]
    public async Task UserSettings_GetProfile_Returns_Defaults()
    {
        var api = CreateClient();
        await EnsureAuthenticatedAsync(api);

        var profile = await api.UserSettings_GetProfileAsync();
        profile.Should().NotBeNull();
        // defaults: no language, no timezone, no API key
        profile!.HasAlphaVantageApiKey.Should().BeFalse();
        profile.ShareAlphaVantageApiKey.Should().BeFalse();
    }

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
            ShareAlphaVantageApiKey: null));
        ok.Should().BeTrue();

        var profile = await api.UserSettings_GetProfileAsync();
        profile.Should().NotBeNull();
        profile!.PreferredLanguage.Should().Be("de");
        profile.TimeZoneId.Should().Be("Europe/Berlin");
    }

    [Fact]
    public async Task UserSettings_GetNotifications_Returns_Defaults()
    {
        var api = CreateClient();
        await EnsureAuthenticatedAsync(api);

        var notifications = await api.User_GetNotificationSettingsAsync();
        notifications.Should().NotBeNull();
        notifications!.MonthlyReminderEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task UserSettings_UpdateNotifications_Works()
    {
        var api = CreateClient();
        await EnsureAuthenticatedAsync(api);

        var ok = await api.User_UpdateNotificationSettingsAsync(
            monthlyEnabled: true,
            hour: 10,
            minute: 30,
            provider: "Memory",
            country: null,
            subdivision: null);
        ok.Should().BeTrue();

        var notifications = await api.User_GetNotificationSettingsAsync();
        notifications.Should().NotBeNull();
        notifications!.MonthlyReminderEnabled.Should().BeTrue();
        notifications.MonthlyReminderHour.Should().Be(10);
        notifications.MonthlyReminderMinute.Should().Be(30);
    }

    [Fact]
    public async Task UserSettings_GetImportSplit_Returns_Defaults()
    {
        var api = CreateClient();
        await EnsureAuthenticatedAsync(api);

        var split = await api.UserSettings_GetImportSplitAsync();
        split.Should().NotBeNull();
        split!.Mode.Should().Be(ImportSplitMode.MonthlyOrFixed);
        split.MaxEntriesPerDraft.Should().Be(250);
    }

    [Fact]
    public async Task UserSettings_UpdateImportSplit_Works()
    {
        var api = CreateClient();
        await EnsureAuthenticatedAsync(api);

        var ok = await api.UserSettings_UpdateImportSplitAsync(new ImportSplitSettingsUpdateRequest(
            Mode: ImportSplitMode.FixedSize,
            MaxEntriesPerDraft: 100,
            MonthlySplitThreshold: null,
            MinEntriesPerDraft: 5));
        ok.Should().BeTrue();

        var split = await api.UserSettings_GetImportSplitAsync();
        split.Should().NotBeNull();
        split!.Mode.Should().Be(ImportSplitMode.FixedSize);
        split.MaxEntriesPerDraft.Should().Be(100);
        split.MinEntriesPerDraft.Should().Be(5);
    }
}
