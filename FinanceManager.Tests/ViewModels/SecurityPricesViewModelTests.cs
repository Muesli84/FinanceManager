using FinanceManager.Application;
using FinanceManager.Shared;
using FinanceManager.Shared.Dtos.Admin;
using FinanceManager.Shared.Dtos.Securities;
using FinanceManager.Web.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace FinanceManager.Tests.ViewModels;

public sealed class SecurityPricesViewModelTests
{
    private sealed class TestCurrentUserService : ICurrentUserService
    {
        public Guid UserId { get; set; } = Guid.NewGuid();
        public string? PreferredLanguage { get; set; }
        public bool IsAuthenticated { get; set; } = true;
        public bool IsAdmin { get; set; } = false;
    }

    private static (SecurityPricesViewModel vm, Mock<IApiClient> apiMock) CreateVm()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentUserService>(new TestCurrentUserService());
        var apiMock = new Mock<IApiClient>();
        services.AddSingleton(apiMock.Object);
        var sp = services.BuildServiceProvider();
        var vm = new SecurityPricesViewModel(sp);
        return (vm, apiMock);
    }

    private static List<SecurityPriceDto> CreatePrices(int count, DateTime? start = null)
    {
        var s = start ?? new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        return Enumerable.Range(0, count)
            .Select(i => new SecurityPriceDto(s.AddDays(i), 100 + i))
            .ToList();
    }

    [Fact]
    public async Task Initialize_LoadsFirstPage_SetsItemsAndFlags()
    {
        var (vm, apiMock) = CreateVm();
        apiMock.Setup(a => a.Securities_GetPricesAsync(It.IsAny<Guid>(), 0, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreatePrices(2));

        var events = 0;
        vm.StateChanged += (_, __) => events++;
        vm.ForSecurity(Guid.NewGuid());

        await vm.InitializeAsync();

        Assert.False(vm.Loading);
        Assert.Equal(2, vm.Items.Count);
        Assert.False(vm.CanLoadMore);
        Assert.True(events >= 2);
    }

    [Fact]
    public async Task LoadMore_AppendsItems_StopsWhenBelowPageSize()
    {
        var (vm, apiMock) = CreateVm();
        int call = 0;
        apiMock.Setup(a => a.Securities_GetPricesAsync(It.IsAny<Guid>(), It.IsAny<int>(), 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                call++;
                return CreatePrices(call == 1 ? 100 : 50);
            });

        vm.ForSecurity(Guid.NewGuid());

        await vm.InitializeAsync();
        Assert.Equal(100, vm.Items.Count);
        Assert.True(vm.CanLoadMore);

        await vm.LoadMoreAsync();
        Assert.Equal(150, vm.Items.Count);
        Assert.False(vm.CanLoadMore);
        Assert.False(vm.Loading);
    }

    [Fact]
    public void OpenBackfillDialog_SetsDefaultsAndOpens()
    {
        var (vm, _) = CreateVm();

        vm.OpenBackfillDialogDefaultPeriod();

        Assert.True(vm.ShowBackfillDialog);
        Assert.NotNull(vm.FromDate);
        Assert.NotNull(vm.ToDate);
        Assert.False(vm.Submitting);
        Assert.Null(vm.DialogErrorKey);
    }

    [Fact]
    public async Task ConfirmBackfill_ValidationErrors()
    {
        var (vm, _) = CreateVm();
        vm.ForSecurity(Guid.NewGuid());

        await vm.ConfirmBackfillAsync();
        Assert.Equal("Dlg_InvalidDates", vm.DialogErrorKey);

        vm.FromDate = DateTime.UtcNow.Date;
        vm.ToDate = vm.FromDate.Value.AddDays(-1);
        await vm.ConfirmBackfillAsync();
        Assert.Equal("Dlg_FromAfterTo", vm.DialogErrorKey);

        vm.ToDate = DateTime.UtcNow.Date.AddDays(1);
        await vm.ConfirmBackfillAsync();
        Assert.Equal("Dlg_ToInFuture", vm.DialogErrorKey);
    }

    [Fact]
    public async Task ConfirmBackfill_PostsAndCloses_OnSuccess()
    {
        var (vm, apiMock) = CreateVm();
        var backfillInfo = new BackgroundTaskInfo(
            Guid.NewGuid(),
            BackgroundTaskType.SecurityPricesBackfill,
            Guid.NewGuid(),
            DateTime.UtcNow,
            BackgroundTaskStatus.Queued,
            null, null, null, 0, 0, null, null, null, null, null, null, null);

        apiMock.Setup(a => a.Securities_EnqueueBackfillAsync(It.IsAny<Guid?>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(backfillInfo);

        vm.ForSecurity(Guid.NewGuid());
        vm.OpenBackfillDialogDefaultPeriod();

        await vm.ConfirmBackfillAsync();

        Assert.False(vm.ShowBackfillDialog);
        Assert.Null(vm.DialogErrorKey);
        apiMock.Verify(a => a.Securities_EnqueueBackfillAsync(It.IsAny<Guid?>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ConfirmBackfill_SetsError_OnFailure()
    {
        var (vm, apiMock) = CreateVm();
        apiMock.Setup(a => a.Securities_EnqueueBackfillAsync(It.IsAny<Guid?>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("fail"));

        vm.ForSecurity(Guid.NewGuid());
        vm.OpenBackfillDialogDefaultPeriod();

        await vm.ConfirmBackfillAsync();

        Assert.True(vm.ShowBackfillDialog);
        Assert.Equal("Dlg_EnqueueFailed", vm.DialogErrorKey);
        Assert.False(vm.Submitting);
    }
}
