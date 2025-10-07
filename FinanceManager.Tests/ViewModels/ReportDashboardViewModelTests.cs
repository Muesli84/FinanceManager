using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FinanceManager.Web.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using FinanceManager.Application;
using FinanceManager.Domain.Reports;

namespace FinanceManager.Tests.ViewModels;

public sealed class ReportDashboardViewModelTests
{
    private static HttpClient CreateHttpClient(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        return new HttpClient(new DelegateHandler(responder)) { BaseAddress = new Uri("http://localhost") };
    }

    private sealed class DelegateHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public DelegateHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_responder(request));
    }

    private sealed class TestHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;
        public TestHttpClientFactory(HttpClient client) => _client = client;
        public HttpClient CreateClient(string name) => _client;
    }

    private sealed class TestCurrentUserService : ICurrentUserService
    {
        public Guid UserId { get; set; } = Guid.NewGuid();
        public string? PreferredLanguage { get; set; }
        public bool IsAuthenticated { get; set; } = true;
        public bool IsAdmin { get; set; }
    }

    private static IServiceProvider CreateSp(bool authenticated = true)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentUserService>(new TestCurrentUserService { IsAuthenticated = authenticated });
        return services.BuildServiceProvider();
    }

    private static string AggregationJson(int points)
    {
        var start = new DateTime(2024,1,1,0,0,0,DateTimeKind.Utc);
        var arr = Enumerable.Range(0, points)
            .Select(i => new
            {
                PeriodStart = start.AddMonths(i).ToString("O"),
                GroupKey = "Type:Bank",
                GroupName = "Bank",
                CategoryName = (string?)null,
                Amount = 100 + i,
                ParentGroupKey = (string?)null,
                PreviousAmount = (decimal?)null,
                YearAgoAmount = (decimal?)null
            })
            .ToArray();
        var obj = new { Interval = 0, Points = arr, ComparedPrevious = false, ComparedYear = false };
        return JsonSerializer.Serialize(obj);
    }
    
    private static string AggregationJsonFrom(params object[] pointAnon)
    {
        var obj = new
        {
            Interval = 0,
            Points = pointAnon,
            ComparedPrevious = false,
            ComparedYear = false
        };
        return JsonSerializer.Serialize(obj);
    }

    [Fact]
    public async Task LoadAsync_ReturnsPoints()
    {
        var client = CreateHttpClient(req =>
        {
            if (req.Method == HttpMethod.Post && req.RequestUri!.AbsolutePath == "/api/report-aggregates")
            {
                var json = AggregationJson(3);
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var vm = new ReportDashboardViewModel(CreateSp(), new TestHttpClientFactory(client));

        var resp = await vm.LoadAsync(0, 0, 24, false, false, false, new int[]{0}, DateTime.UtcNow, null);

        Assert.Equal(3, resp.Points.Count);
    }

    [Fact]
    public async Task SaveUpdateDelete_Favorites_Roundtrip()
    {
        HttpRequestMessage? lastReq = null;
        int postCount = 0;
        var client = CreateHttpClient(req =>
        {
            lastReq = req;
            if (req.Method == HttpMethod.Post && req.RequestUri!.AbsolutePath == "/api/report-favorites")
            {
                postCount++;
                var json = JsonSerializer.Serialize(new ReportDashboardViewModel.FavoriteDto(Guid.NewGuid(), "Fav", 0, false, 0, 24, false, false, true, true, DateTime.UtcNow, null, new int[]{0}, null));
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
            }
            if (req.Method == HttpMethod.Put && req.RequestUri!.AbsolutePath.StartsWith("/api/report-favorites/"))
            {
                var json = JsonSerializer.Serialize(new ReportDashboardViewModel.FavoriteDto(Guid.NewGuid(), "Fav2", 0, false, 0, 24, false, false, true, true, DateTime.UtcNow, null, new int[]{0}, null));
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
            }
            if (req.Method == HttpMethod.Delete && req.RequestUri!.AbsolutePath.StartsWith("/api/report-favorites/"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK);
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var vm = new ReportDashboardViewModel(CreateSp(), new TestHttpClientFactory(client));

        var saved = await vm.SaveFavoriteAsync("n", 0, false, 0, 24, false, false, true, true, new int[]{0}, null);
        Assert.NotNull(saved);
        Assert.Equal(HttpMethod.Post, lastReq!.Method);
        Assert.Equal("/api/report-favorites", lastReq!.RequestUri!.AbsolutePath);

        var updated = await vm.UpdateFavoriteAsync(Guid.NewGuid(), "n2", 0, false, 0, 24, false, false, true, true, new int[]{0}, null);
        Assert.NotNull(updated);
        Assert.Equal(HttpMethod.Put, lastReq!.Method);

        var deleted = await vm.DeleteFavoriteAsync(Guid.NewGuid());
        Assert.True(deleted);
    }
    
    [Fact]
    public async Task GetChartByPeriod_ComputesSums_PerMonth()
    {
        var start = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var json = AggregationJsonFrom(
            new { PeriodStart = start.ToString("O"), GroupKey = "Type:Bank", GroupName = "Bank", CategoryName = (string?)null, Amount = 100m, ParentGroupKey = (string?)null, PreviousAmount = (decimal?)null, YearAgoAmount = (decimal?)null },
            new { PeriodStart = start.ToString("O"), GroupKey = "Type:Contact", GroupName = "Contact", CategoryName = (string?)null, Amount = 50m, ParentGroupKey = (string?)null, PreviousAmount = (decimal?)null, YearAgoAmount = (decimal?)null },
            new { PeriodStart = start.AddMonths(1).ToString("O"), GroupKey = "Type:Bank", GroupName = "Bank", CategoryName = (string?)null, Amount = 200m, ParentGroupKey = (string?)null, PreviousAmount = (decimal?)null, YearAgoAmount = (decimal?)null }
        );

        var client = CreateHttpClient(req =>
        {
            if (req.Method == HttpMethod.Post && req.RequestUri!.AbsolutePath == "/api/report-aggregates")
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var vm = new ReportDashboardViewModel(CreateSp(), new TestHttpClientFactory(client))
        {
            SelectedKinds = new List<int> { 0, 1 }, // Bank, Contact
            Interval = (int)ReportInterval.Month,
            IncludeCategory = false,
            Take = 24
        };

        await vm.ReloadAsync(start);
        var byPeriod = vm.GetChartByPeriod();
        Assert.Equal(2, byPeriod.Count);
        Assert.Equal(150m, byPeriod[0].Sum); // 100 + 50
        Assert.Equal(200m, byPeriod[1].Sum);
    }
    
    [Fact]
    public async Task Totals_And_ColumnVisibility_Work()
    {
        var start = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        // Two type points with previous/year amounts
        var json = AggregationJsonFrom(
            new { PeriodStart = start.ToString("O"), GroupKey = "Type:Bank", GroupName = "Bank", CategoryName = (string?)null, Amount = 120m, ParentGroupKey = (string?)null, PreviousAmount = 100m, YearAgoAmount = 80m },
            new { PeriodStart = start.ToString("O"), GroupKey = "Type:Contact", GroupName = "Contact", CategoryName = (string?)null, Amount = 30m, ParentGroupKey = (string?)null, PreviousAmount = 25m, YearAgoAmount = 20m }
        );

        var client = CreateHttpClient(req =>
        {
            if (req.Method == HttpMethod.Post && req.RequestUri!.AbsolutePath == "/api/report-aggregates")
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var vm = new ReportDashboardViewModel(CreateSp(), new TestHttpClientFactory(client))
        {
            SelectedKinds = new List<int> { 0, 1 }, // multi
            IncludeCategory = true,
            ComparePrevious = true,
            CompareYear = true,
            Interval = (int)ReportInterval.Month
        };

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
        var p = new ReportDashboardViewModel.PointDto(DateTime.UtcNow, "x", "n", null, 0m, null, -10m, -5m);
        Assert.True(ReportDashboardViewModel.IsNegative(p));
    }
    
    [Fact]
    public async Task PerType_Children_When_IncludeCategory_Multi()
    {
        var start = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var json = AggregationJsonFrom(
            // Top-level types
            new { PeriodStart = start.ToString("O"), GroupKey = "Type:Bank", GroupName = "Bank", CategoryName = (string?)null, Amount = 100m, ParentGroupKey = (string?)null, PreviousAmount = (decimal?)null, YearAgoAmount = (decimal?)null },
            new { PeriodStart = start.ToString("O"), GroupKey = "Type:Contact", GroupName = "Contact", CategoryName = (string?)null, Amount = 50m, ParentGroupKey = (string?)null, PreviousAmount = (decimal?)null, YearAgoAmount = (decimal?)null },
            // Bank children as entities
            new { PeriodStart = start.ToString("O"), GroupKey = "Account:acc1", GroupName = "Checking", CategoryName = (string?)null, Amount = 60m, ParentGroupKey = "Type:Bank", PreviousAmount = (decimal?)null, YearAgoAmount = (decimal?)null },
            // Contact children as categories
            new { PeriodStart = start.ToString("O"), GroupKey = "Category:Contact:Food", GroupName = "Food", CategoryName = "Food", Amount = 50m, ParentGroupKey = "Type:Contact", PreviousAmount = (decimal?)null, YearAgoAmount = (decimal?)null }
        );

        var client = CreateHttpClient(req =>
        {
            if (req.Method == HttpMethod.Post && req.RequestUri!.AbsolutePath == "/api/report-aggregates")
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var vm = new ReportDashboardViewModel(CreateSp(), new TestHttpClientFactory(client))
        {
            SelectedKinds = new List<int> { 0, 1 },
            IncludeCategory = true,
        };

        await vm.ReloadAsync(start);
        Assert.True(vm.HasChildren("Type:Bank"));
        Assert.True(vm.HasChildren("Type:Contact"));
        var bankChildren = vm.GetChildRows("Type:Bank").ToList();
        Assert.All(bankChildren, c => Assert.False(c.GroupKey.StartsWith("Category:")));
        var contactChildren = vm.GetChildRows("Type:Contact").ToList();
        Assert.All(contactChildren, c => Assert.True(c.GroupKey.StartsWith("Category:")));
    }
}
