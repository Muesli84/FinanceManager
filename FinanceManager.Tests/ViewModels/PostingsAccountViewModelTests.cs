using FinanceManager.Application;
using FinanceManager.Web.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Text;
using System.Text.Json;

namespace FinanceManager.Tests.ViewModels;

public sealed class PostingsAccountViewModelTests
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

    private static string PostingsJson(int count)
    {
        var arr = Enumerable.Range(0, count)
            .Select(i => new
            {
                Id = Guid.NewGuid(),
                BookingDate = DateTime.UtcNow.Date.AddDays(-i).ToString("O"),
                Amount = 100 + i,
                Kind = 0, // Bank
                AccountId = Guid.NewGuid(),
                ContactId = (Guid?)null,
                SavingsPlanId = (Guid?)null,
                SecurityId = (Guid?)null,
                SourceId = Guid.NewGuid(),
                Subject = $"S{i}",
                RecipientName = $"R{i}",
                Description = $"D{i}",
                SecuritySubType = (int?)null,
                Quantity = (decimal?)null,
                GroupId = Guid.NewGuid()
            })
            .ToArray();
        return JsonSerializer.Serialize(arr);
    }

    [Fact]
    public async Task Initialize_LoadsFirstPage_SetsItemsAndFlags()
    {
        var client = CreateHttpClient(req =>
        {
            if (req.Method == HttpMethod.Get && req.RequestUri!.AbsolutePath.StartsWith("/api/postings/account/"))
            {
                var json = PostingsJson(10);
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var vm = new PostingsAccountViewModel(CreateSp(), new TestHttpClientFactory(client));
        vm.Configure(Guid.NewGuid());

        await vm.InitializeAsync();

        Assert.False(vm.Loading);
        Assert.Equal(10, vm.Items.Count);
        Assert.True(vm.CanLoadMore); // page size 50, got 10
    }

    [Fact]
    public async Task LoadMore_AppendsItems_StopsWhenBelowPageSize()
    {
        int call = 0;
        var client = CreateHttpClient(req =>
        {
            if (req.RequestUri!.AbsolutePath.StartsWith("/api/postings/account/") && req.Method == HttpMethod.Get)
            {
                call++;
                var cnt = call == 1 ? 50 : 3; // first page full, second short
                var json = PostingsJson(cnt);
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var vm = new PostingsAccountViewModel(CreateSp(), new TestHttpClientFactory(client));
        vm.Configure(Guid.NewGuid());

        await vm.InitializeAsync();
        Assert.Equal(50, vm.Items.Count);
        Assert.True(vm.CanLoadMore);

        await vm.LoadMoreAsync();
        Assert.Equal(53, vm.Items.Count);
        Assert.False(vm.CanLoadMore);
    }

    [Fact]
    public void GetExportUrl_ComposesQuery()
    {
        var vm = new PostingsAccountViewModel(CreateSp(), new TestHttpClientFactory(CreateHttpClient(_ => new HttpResponseMessage(HttpStatusCode.OK))));
        vm.Configure(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        vm.SetSearch("test q");
        vm.SetRange(new DateTime(2024, 1, 2), new DateTime(2024, 2, 3));

        var url = vm.GetExportUrl("csv");

        Assert.StartsWith("/api/postings/account/11111111-1111-1111-1111-111111111111/export", url);
        Assert.Contains("format=csv", url);
        Assert.Contains("q=test%20q", url);
        Assert.Contains("from=2024-01-02", url);
        Assert.Contains("to=2024-02-03", url);
    }
}
