using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FinanceManager.Web.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using FinanceManager.Application;

namespace FinanceManager.Tests.ViewModels;

public sealed class SecurityPricesViewModelTests
{
    private static HttpClient CreateHttpClient(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        return new HttpClient(new DelegateHandler(responder))
        {
            BaseAddress = new Uri("http://localhost")
        };
    }

    private sealed class DelegateHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public DelegateHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_responder(request));
        }
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
        public bool IsAdmin { get; set; } = false;
    }

    private static IServiceProvider CreateSp()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentUserService>(new TestCurrentUserService());
        return services.BuildServiceProvider();
    }

    private static string PricesJson(int count, DateTime? start = null)
    {
        var s = start ?? new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var arr = Enumerable.Range(0, count)
            .Select(i => new { Date = s.AddDays(i).ToString("O"), Close = (decimal)(100 + i) })
            .ToArray();
        return JsonSerializer.Serialize(arr);
    }

    [Fact]
    public async Task Initialize_LoadsFirstPage_SetsItemsAndFlags()
    {
        // arrange: first call returns 2 items (< page size), making CanLoadMore=false
        var client = CreateHttpClient(req =>
        {
            if (req.Method == HttpMethod.Get && req.RequestUri!.AbsolutePath.StartsWith("/api/securities/"))
            {
                var json = PricesJson(2);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var factory = new TestHttpClientFactory(client);
        var sp = CreateSp();
        var vm = new SecurityPricesViewModel(sp, factory);
        var events = 0;
        vm.StateChanged += (_, __) => events++;
        vm.ForSecurity(Guid.NewGuid());

        // act
        await vm.InitializeAsync();

        // assert
        Assert.False(vm.Loading);
        Assert.Equal(2, vm.Items.Count);
        Assert.False(vm.CanLoadMore); // because < 100 returned
        Assert.True(events >= 2); // Loading true -> false at least two raises
    }

    [Fact]
    public async Task LoadMore_AppendsItems_StopsWhenBelowPageSize()
    {
        // arrange: first GET returns 100 items, second returns 50
        var call = 0;
        var client = CreateHttpClient(req =>
        {
            if (req.Method == HttpMethod.Get && req.RequestUri!.AbsolutePath.StartsWith("/api/securities/"))
            {
                call++;
                var cnt = call == 1 ? 100 : 50;
                var json = PricesJson(cnt);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var factory = new TestHttpClientFactory(client);
        var sp = CreateSp();
        var vm = new SecurityPricesViewModel(sp, factory);
        vm.ForSecurity(Guid.NewGuid());

        // act + assert first load
        await vm.InitializeAsync();
        Assert.Equal(100, vm.Items.Count);
        Assert.True(vm.CanLoadMore);

        // act second load
        await vm.LoadMoreAsync();
        Assert.Equal(150, vm.Items.Count);
        Assert.False(vm.CanLoadMore);
        Assert.False(vm.Loading);
    }

    [Fact]
    public void OpenBackfillDialog_SetsDefaultsAndOpens()
    {
        var factory = new TestHttpClientFactory(CreateHttpClient(_ => new HttpResponseMessage(HttpStatusCode.OK)));
        var sp = CreateSp();
        var vm = new SecurityPricesViewModel(sp, factory);

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
        var factory = new TestHttpClientFactory(CreateHttpClient(_ => new HttpResponseMessage(HttpStatusCode.OK)));
        var sp = CreateSp();
        var vm = new SecurityPricesViewModel(sp, factory);
        vm.ForSecurity(Guid.NewGuid());

        // no dates
        await vm.ConfirmBackfillAsync();
        Assert.Equal("Dlg_InvalidDates", vm.DialogErrorKey);

        // from > to
        vm.FromDate = DateTime.UtcNow.Date;
        vm.ToDate = vm.FromDate.Value.AddDays(-1);
        await vm.ConfirmBackfillAsync();
        Assert.Equal("Dlg_FromAfterTo", vm.DialogErrorKey);

        // to in future
        vm.ToDate = DateTime.UtcNow.Date.AddDays(1);
        await vm.ConfirmBackfillAsync();
        Assert.Equal("Dlg_ToInFuture", vm.DialogErrorKey);
    }

    [Fact]
    public async Task ConfirmBackfill_PostsAndCloses_OnSuccess()
    {
        HttpRequestMessage? last = null;
        var client = CreateHttpClient(req =>
        {
            last = req;
            if (req.Method == HttpMethod.Post && req.RequestUri!.AbsolutePath == "/api/securities/backfill")
            {
                return new HttpResponseMessage(HttpStatusCode.OK);
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(PricesJson(0), Encoding.UTF8, "application/json")
            };
        });
        var factory = new TestHttpClientFactory(client);
        var sp = CreateSp();
        var vm = new SecurityPricesViewModel(sp, factory);
        vm.ForSecurity(Guid.NewGuid());
        vm.OpenBackfillDialogDefaultPeriod();

        await vm.ConfirmBackfillAsync();

        Assert.False(vm.ShowBackfillDialog);
        Assert.Null(vm.DialogErrorKey);
        Assert.NotNull(last);
        Assert.Equal(HttpMethod.Post, last!.Method);
        Assert.Equal("/api/securities/backfill", last!.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task ConfirmBackfill_SetsError_OnFailure()
    {
        var client = CreateHttpClient(req => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var factory = new TestHttpClientFactory(client);
        var sp = CreateSp();
        var vm = new SecurityPricesViewModel(sp, factory);
        vm.ForSecurity(Guid.NewGuid());
        vm.OpenBackfillDialogDefaultPeriod();

        await vm.ConfirmBackfillAsync();

        Assert.True(vm.ShowBackfillDialog); // remains open
        Assert.Equal("Dlg_EnqueueFailed", vm.DialogErrorKey);
        Assert.False(vm.Submitting);
    }
}
