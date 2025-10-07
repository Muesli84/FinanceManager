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

public sealed class ReportsHomeViewModelTests
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

    private static string FavoritesJson(int count)
    {
        var arr = Enumerable.Range(0, count)
            .Select(i => new
            {
                Id = Guid.NewGuid(),
                Name = $"Fav {count - i}",
                PostingKind = 0,
                IncludeCategory = false,
                Interval = 0,
                ComparePrevious = false,
                CompareYear = false,
                ShowChart = true,
                Expandable = true,
                CreatedUtc = DateTime.UtcNow.AddDays(-i).ToString("O"),
                ModifiedUtc = (string?)null,
                PostingKinds = new int[] { 0 }
            })
            .ToArray();
        return JsonSerializer.Serialize(arr);
    }

    [Fact]
    public async Task Initialize_LoadsFavorites_SortsByName()
    {
        var client = CreateHttpClient(req =>
        {
            if (req.Method == HttpMethod.Get && req.RequestUri!.AbsolutePath == "/api/report-favorites")
            {
                var json = FavoritesJson(3);
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var vm = new ReportsHomeViewModel(CreateSp(), new TestHttpClientFactory(client));

        await vm.InitializeAsync();

        Assert.False(vm.Loading);
        Assert.Equal(3, vm.Favorites.Count);
        Assert.Collection(vm.Favorites,
            a => Assert.Equal("Fav 1", a.Name),
            b => Assert.Equal("Fav 2", b.Name),
            c => Assert.Equal("Fav 3", c.Name));
    }

    [Fact]
    public async Task Reload_DoesNotThrow_OnError()
    {
        var client = CreateHttpClient(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var vm = new ReportsHomeViewModel(CreateSp(), new TestHttpClientFactory(client));

        await vm.InitializeAsync();
        await vm.ReloadAsync();

        Assert.False(vm.Loading);
    }
}
