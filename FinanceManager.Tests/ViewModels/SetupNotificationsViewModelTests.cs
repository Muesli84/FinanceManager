using FinanceManager.Application;
using FinanceManager.Shared;
using FinanceManager.Web.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace FinanceManager.Tests.ViewModels;

public sealed class SetupNotificationsViewModelTests
{
    private sealed class TestCurrentUserService : ICurrentUserService
    {
        public Guid UserId { get; set; } = Guid.NewGuid();
        public string? PreferredLanguage { get; set; }
        public bool IsAuthenticated { get; set; } = true;
        public bool IsAdmin { get; set; }
    }

    private static (SetupNotificationsViewModel vm, Mock<IApiClient> apiMock) CreateVm()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentUserService>(new TestCurrentUserService());
        var apiMock = new Mock<IApiClient>();
        services.AddSingleton(apiMock.Object);
        var sp = services.BuildServiceProvider();
        var vm = new SetupNotificationsViewModel(sp);
        return (vm, apiMock);
    }

    [Fact]
    public async Task Initialize_Loads_Settings_And_Subdivisions()
    {
        var (vm, apiMock) = CreateVm();
        var dto = new NotificationSettingsDto
        {
            MonthlyReminderEnabled = true,
            MonthlyReminderHour = 8,
            MonthlyReminderMinute = 30,
            HolidayProvider = "NagerDate",
            HolidayCountryCode = "DE"
        };
        var subs = new[] { "BW", "BY" };

        apiMock.Setup(a => a.User_GetNotificationSettingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(dto);
        apiMock.Setup(a => a.Meta_GetHolidaySubdivisionsAsync("NagerDate", "DE", It.IsAny<CancellationToken>()))
            .ReturnsAsync(subs);

        await vm.InitializeAsync();

        Assert.False(vm.Loading);
        Assert.True(vm.Model.MonthlyReminderEnabled);
        Assert.Equal(8, vm.Hour);
        Assert.Equal(30, vm.Minute);
        Assert.NotNull(vm.Subdivisions);
        Assert.Contains("BW", vm.Subdivisions);
    }

    [Fact]
    public async Task ProviderChange_Memory_Clears_Subdivision_And_Dirty()
    {
        var (vm, apiMock) = CreateVm();
        var dto = new NotificationSettingsDto
        {
            HolidayProvider = "NagerDate",
            HolidayCountryCode = "DE",
            HolidaySubdivisionCode = "BW"
        };

        apiMock.Setup(a => a.User_GetNotificationSettingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(dto);

        await vm.InitializeAsync();

        vm.Model.HolidayProvider = "Memory";
        await vm.OnProviderChanged();

        Assert.Null(vm.Model.HolidaySubdivisionCode);
        Assert.True(vm.Dirty);
    }

    [Fact]
    public async Task Save_Sets_SavedOk_And_Resets_Dirty()
    {
        var (vm, apiMock) = CreateVm();
        var dto = new NotificationSettingsDto
        {
            MonthlyReminderEnabled = false,
            MonthlyReminderHour = 9,
            MonthlyReminderMinute = 0,
            HolidayProvider = "Memory"
        };

        apiMock.Setup(a => a.User_GetNotificationSettingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(dto);
        apiMock.Setup(a => a.User_UpdateNotificationSettingsAsync(
                It.IsAny<bool>(), It.IsAny<int?>(), It.IsAny<int?>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await vm.InitializeAsync();

        vm.Model.MonthlyReminderEnabled = true;
        vm.Hour = 10;
        vm.Minute = 15;
        vm.OnChanged();
        Assert.True(vm.Dirty);

        await vm.SaveAsync();

        apiMock.Verify(a => a.User_UpdateNotificationSettingsAsync(
            true, 10, 15, "Memory", null, null, It.IsAny<CancellationToken>()), Times.Once);
        Assert.True(vm.SavedOk);
        Assert.False(vm.Dirty);
    }
}
