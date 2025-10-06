using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FinanceManager.Application;
using FinanceManager.Web.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Xunit;

namespace FinanceManager.Tests.ViewModels;

public sealed class SavingsPlanCategoriesViewModelTests
{
    private static HttpClient CreateHttpClient(Func<HttpRequestMessage, HttpResponseMessage> responder)
        => new HttpClient(new DelegateHandler(responder)) { BaseAddress = new Uri("http://localhost") };

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

    private static string CatsJson(params object[] cats) => JsonSerializer.Serialize(cats);

    [Fact]
    public async Task Initialize_Loads_Categories()
    {
        var c1 = new { Id = Guid.NewGuid(), Name = "A" };
        var c2 = new { Id = Guid.NewGuid(), Name = "B" };
        var client = CreateHttpClient(req =>
        {
            if (req.Method == HttpMethod.Get && req.RequestUri!.AbsolutePath == "/api/savings-plan-categories")
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(CatsJson(c1, c2), Encoding.UTF8, "application/json") };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var vm = new SavingsPlanCategoriesViewModel(CreateSp(), new TestHttpClientFactory(client));
        await vm.InitializeAsync();

        Assert.True(vm.Loaded);
        Assert.Equal(2, vm.Categories.Count);
        Assert.Equal(new[] { "A", "B" }, vm.Categories.Select(x => x.Name).OrderBy(x => x).ToArray());
    }

    [Fact]
    public async Task Initialize_RequiresAuth_When_NotAuthenticated()
    {
        var vm = new SavingsPlanCategoriesViewModel(CreateSp(authenticated: false), new TestHttpClientFactory(CreateHttpClient(_ => new HttpResponseMessage(HttpStatusCode.NotFound))));
        bool authRequired = false;
        vm.AuthenticationRequired += (_, __) => authRequired = true;
        await vm.InitializeAsync();
        Assert.False(vm.Loaded);
        Assert.True(authRequired);
    }

    [Fact]
    public void Ribbon_Has_Actions()
    {
        var vm = new SavingsPlanCategoriesViewModel(CreateSp(), new TestHttpClientFactory(CreateHttpClient(_ => new HttpResponseMessage(HttpStatusCode.NotFound))));
        var loc = new TestLocalizer<SavingsPlanCategoriesViewModelTests>();
        var groups = vm.GetRibbon(loc);
        var actions = groups.First();
        Assert.Contains(actions.Items, i => i.Action == "New");
        Assert.Contains(actions.Items, i => i.Action == "Back");
    }

    private sealed class TestLocalizer<T> : IStringLocalizer<T>
    {
        public LocalizedString this[string name] => new(name, name);
        public LocalizedString this[string name, params object[] arguments] => new(name, string.Format(name, arguments));
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) { yield break; }
        public IStringLocalizer WithCulture(System.Globalization.CultureInfo culture) => this;
    }
}
