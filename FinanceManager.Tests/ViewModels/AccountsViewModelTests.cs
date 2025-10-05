using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using FinanceManager.Application;
using FinanceManager.Web.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Xunit;

namespace FinanceManager.Tests.ViewModels;

public sealed class AccountsViewModelTests
{
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
        public Guid UserId => Guid.NewGuid();
        public string? PreferredLanguage => "de";
        public bool IsAuthenticated { get; set; } = true;
        public bool IsAdmin => false;
    }

    private sealed class DummyLocalizer : IStringLocalizer
    {
        public LocalizedString this[string name] => new(name, name);
        public LocalizedString this[string name, params object[] arguments] => new(name, name);
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => Array.Empty<LocalizedString>();
        public IStringLocalizer WithCulture(System.Globalization.CultureInfo culture) => this;
    }

    private static HttpClient CreateHttpClient(Func<HttpRequestMessage, HttpResponseMessage> responder)
        => new(new DelegateHandler(responder)) { BaseAddress = new Uri("http://localhost") };

    private static AccountsViewModel CreateVm(HttpClient client, bool isAuthenticated = true)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentUserService>(new TestCurrentUserService { IsAuthenticated = isAuthenticated });
        var sp = services.BuildServiceProvider();
        var factory = new TestHttpClientFactory(client);
        var vm = new AccountsViewModel(sp, factory);
        return vm;
    }

    private static string AccountsJson(params (Guid id, string name, int type, string? iban, decimal balance)[] items)
    {
        var arr = items.Select(a => new { Id = a.id, Name = a.name, Type = a.type, Iban = a.iban, CurrentBalance = a.balance, BankContactId = Guid.NewGuid() }).ToArray();
        return JsonSerializer.Serialize(arr);
    }

    [Fact]
    public async Task Initialize_LoadsAccounts_WhenAuthenticated()
    {
        // arrange
        var client = CreateHttpClient(req =>
        {
            if (req.Method == HttpMethod.Get && req.RequestUri!.AbsolutePath == "/api/accounts")
            {
                var json = AccountsJson((Guid.NewGuid(), "A", (int)AccountType.Giro, "DE00", 10m), (Guid.NewGuid(), "B", (int)AccountType.Savings, null, 20m));
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var vm = CreateVm(client, isAuthenticated: true);

        // act
        await vm.InitializeAsync();

        // assert
        Assert.True(vm.Loaded);
        Assert.Equal(2, vm.Accounts.Count);
        Assert.All(vm.Accounts, a => Assert.False(string.IsNullOrWhiteSpace(a.Name)));
    }

    [Fact]
    public async Task Initialize_RequiresAuth_WhenNotAuthenticated()
    {
        // arrange
        var called = false;
        var client = CreateHttpClient(_ => { called = true; return new HttpResponseMessage(HttpStatusCode.OK); });
        var vm = CreateVm(client, isAuthenticated: false);
        var authEvents = 0;
        vm.AuthenticationRequired += (_, __) => authEvents++;

        // act
        await vm.InitializeAsync();

        // assert
        Assert.Equal(1, authEvents);
        Assert.False(vm.Loaded);
        Assert.False(called); // no HTTP calls
    }

    [Fact]
    public async Task SetFilter_AffectsLoad_AndRibbon()
    {
        // arrange
        var lastUri = "";
        var client = CreateHttpClient(req =>
        {
            lastUri = req.RequestUri!.ToString();
            var json = AccountsJson();
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        });
        var vm = CreateVm(client);
        var filterId = Guid.NewGuid();
        vm.SetFilter(filterId);

        // act
        await vm.InitializeAsync();

        // assert
        Assert.Contains($"bankContactId={filterId}", lastUri);

        // ribbon includes ClearFilter
        var loc = new DummyLocalizer();
        var groups = vm.GetRibbon(loc);
        Assert.Contains(groups, g => g.Items.Any(i => i.Action == "ClearFilter"));
    }

    [Fact]
    public void GetRibbon_ContainsNew()
    {
        var client = CreateHttpClient(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var vm = CreateVm(client);
        var loc = new DummyLocalizer();
        var groups = vm.GetRibbon(loc);
        Assert.Contains(groups, g => g.Items.Any(i => i.Action == "New"));
    }
}
