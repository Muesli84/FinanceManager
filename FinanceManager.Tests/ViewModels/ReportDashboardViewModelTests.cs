using FinanceManager.Application;
using FinanceManager.Shared;
using FinanceManager.Shared.Dtos.Reports;
using FinanceManager.Web.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace FinanceManager.Tests.ViewModels;

public sealed class ReportDashboardViewModelTests
{
    private sealed class TestCurrentUserService : ICurrentUserService
    {
        public Guid UserId { get; set; } = Guid.NewGuid();
        public string? PreferredLanguage { get; set; }
        public bool IsAuthenticated { get; set; } = true;
        public bool IsAdmin { get; set; }
    }

    private static (ReportDashboardViewModel vm, Mock<IApiClient> apiMock) CreateVm(bool authenticated = true)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentUserService>(new TestCurrentUserService { IsAuthenticated = authenticated });
        var apiMock = new Mock<IApiClient>();
        services.AddSingleton(apiMock.Object);
        var sp = services.BuildServiceProvider();
        var vm = new ReportDashboardViewModel(sp);
        return (vm, apiMock);
    }

    private static List<ReportAggregatePointDto> CreatePoints(int count)
    {
        var start = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        return Enumerable.Range(0, count)
            .Select(i => new ReportAggregatePointDto(start.AddMonths(i), "Type:Bank", "Bank", null, 100 + i, null, null, null))
            .ToList();
    }

    [Fact]
    public async Task LoadAsync_ReturnsPoints()
    {
        var (vm, apiMock) = CreateVm();
        var result = new ReportAggregationResult(ReportInterval.Month, CreatePoints(3), false, false);

        apiMock.Setup(a => a.Reports_QueryAggregatesAsync(It.IsAny<ReportAggregatesQueryRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);

        var resp = await vm.LoadAsync(PostingKind.Bank, 0, 24, false, false, false, new[] { PostingKind.Bank }, DateTime.UtcNow, null);

        Assert.Equal(3, resp.Points.Count);
    }

    [Fact]
    public async Task SaveUpdateDelete_Favorites_Roundtrip()
    {
        var (vm, apiMock) = CreateVm();
        var savedFav = new ReportFavoriteDto(Guid.NewGuid(), "Fav", PostingKind.Bank, false, 0, 24, false, false, true, true, DateTime.UtcNow, null, new[] { PostingKind.Bank }, null, false);
        var updatedFav = new ReportFavoriteDto(Guid.NewGuid(), "Fav2", PostingKind.Bank, false, 0, 24, false, false, true, true, DateTime.UtcNow, null, new[] { PostingKind.Bank }, null, false);

        apiMock.Setup(a => a.Reports_CreateFavoriteAsync(It.IsAny<ReportFavoriteCreateApiRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(savedFav);
        apiMock.Setup(a => a.Reports_UpdateFavoriteAsync(It.IsAny<Guid>(), It.IsAny<ReportFavoriteUpdateApiRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(updatedFav);
        apiMock.Setup(a => a.Reports_DeleteFavoriteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var saved = await vm.SaveFavoriteAsync("n", PostingKind.Bank, false, 0, 24, false, false, true, true, new[] { PostingKind.Bank }, null);
        Assert.NotNull(saved);

        var updated = await vm.UpdateFavoriteAsync(Guid.NewGuid(), "n2", PostingKind.Bank, false, 0, 24, false, false, true, true, new[] { PostingKind.Bank }, null);
        Assert.NotNull(updated);

        var deleted = await vm.DeleteFavoriteAsync(Guid.NewGuid());
        Assert.True(deleted);
    }

    [Fact]
    public async Task GetChartByPeriod_ComputesSums_PerMonth()
    {
        var (vm, apiMock) = CreateVm();
        var start = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var points = new List<ReportAggregatePointDto>
        {
            new ReportAggregatePointDto(start, "Type:Bank", "Bank", null, 100m, null, null, null),
            new ReportAggregatePointDto(start, "Type:Contact", "Contact", null, 50m, null, null, null),
            new ReportAggregatePointDto(start.AddMonths(1), "Type:Bank", "Bank", null, 200m, null, null, null)
        };
        var result = new ReportAggregationResult(ReportInterval.Month, points, false, false);

        apiMock.Setup(a => a.Reports_QueryAggregatesAsync(It.IsAny<ReportAggregatesQueryRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);

        vm.SelectedKinds = new List<PostingKind> { PostingKind.Bank, PostingKind.Contact };
        vm.Interval = (int)ReportInterval.Month;
        vm.IncludeCategory = false;
        vm.Take = 24;

        await vm.ReloadAsync(start);
        var byPeriod = vm.GetChartByPeriod();

        Assert.Equal(2, byPeriod.Count);
        Assert.Equal(150m, byPeriod[0].Sum);
        Assert.Equal(200m, byPeriod[1].Sum);
    }

    [Fact]
    public async Task Totals_And_ColumnVisibility_Work()
    {
        var (vm, apiMock) = CreateVm();
        var start = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var points = new List<ReportAggregatePointDto>
        {
            new ReportAggregatePointDto(start, "Type:Bank", "Bank", null, 120m, null, 100m, 80m),
            new ReportAggregatePointDto(start, "Type:Contact", "Contact", null, 30m, null, 25m, 20m)
        };
        var result = new ReportAggregationResult(ReportInterval.Month, points, true, true);

        apiMock.Setup(a => a.Reports_QueryAggregatesAsync(It.IsAny<ReportAggregatesQueryRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);

        vm.SelectedKinds = new List<PostingKind> { PostingKind.Bank, PostingKind.Contact };
        vm.IncludeCategory = true;
        vm.ComparePrevious = true;
        vm.CompareYear = true;
        vm.Interval = (int)ReportInterval.Month;

        await vm.ReloadAsync(start);

        Assert.True(vm.ShowCategoryColumn);
        Assert.True(vm.ShowPreviousColumns);

        var t = vm.GetTotals();
        Assert.Equal(150m, t.Amount);
        Assert.Equal(125m, t.Prev);
        Assert.Equal(100m, t.Year);
    }

    [Fact]
    public void IsNegative_MarksZeroWithNegativeBaselines()
    {
        var p = new ReportAggregatePointDto(DateTime.UtcNow, "x", "n", null, 0m, null, -10m, -5m);
        Assert.True(ReportDashboardViewModel.IsNegative(p));
    }

    [Fact]
    public async Task PerType_Children_When_IncludeCategory_Multi()
    {
        var (vm, apiMock) = CreateVm();
        var start = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var points = new List<ReportAggregatePointDto>
        {
            new ReportAggregatePointDto(start, "Type:Bank", "Bank", null, 100m, null, null, null),
            new ReportAggregatePointDto(start, "Type:Contact", "Contact", null, 50m, null, null, null),
            new ReportAggregatePointDto(start, "Account:acc1", "Checking", null, 60m, "Type:Bank", null, null),
            new ReportAggregatePointDto(start, "Category:Contact:Food", "Food", "Food", 50m, "Type:Contact", null, null)
        };
        var result = new ReportAggregationResult(ReportInterval.Month, points, false, false);

        apiMock.Setup(a => a.Reports_QueryAggregatesAsync(It.IsAny<ReportAggregatesQueryRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);

        vm.SelectedKinds = new List<PostingKind> { PostingKind.Bank, PostingKind.Contact };
        vm.IncludeCategory = true;

        await vm.ReloadAsync(start);

        Assert.True(vm.HasChildren("Type:Bank"));
        Assert.True(vm.HasChildren("Type:Contact"));
        var bankChildren = vm.GetChildRows("Type:Bank").ToList();
        Assert.All(bankChildren, c => Assert.False(c.GroupKey.StartsWith("Category:")));
        var contactChildren = vm.GetChildRows("Type:Contact").ToList();
        Assert.All(contactChildren, c => Assert.True(c.GroupKey.StartsWith("Category:")));
    }

    [Fact]
    public void IsNegative_Works()
    {
        var p = new ReportAggregatePointDto(DateTime.UtcNow, "x", "n", null, 0m, null, -10m, -5m);
        Assert.True(ReportDashboardViewModel.IsNegative(p));
    }
}
