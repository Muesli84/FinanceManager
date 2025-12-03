using FinanceManager.Application;
using FinanceManager.Web.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using System.Net;
using System.Text;
using System.Text.Json;

namespace FinanceManager.Tests.ViewModels;

public sealed class ContactCategoriesViewModelTests
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

    private static ContactCategoriesViewModel CreateVm(HttpClient client, bool isAuthenticated = true)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentUserService>(new TestCurrentUserService { IsAuthenticated = isAuthenticated });
        var sp = services.BuildServiceProvider();
        var factory = new TestHttpClientFactory(client);
        return new ContactCategoriesViewModel(sp, factory);
    }

    private static string CategoriesJson(params (Guid id, string name)[] items)
    {
        var arr = items.Select(c => new { Id = c.id, Name = c.name }).ToArray();
        return JsonSerializer.Serialize(arr);
    }

    [Fact]
    public async Task Initialize_LoadsCategories_WhenAuthenticated()
    {
        // arrange
        var client = CreateHttpClient(req =>
        {
            if (req.Method == HttpMethod.Get && req.RequestUri!.AbsolutePath == "/api/contact-categories")
            {
                var json = CategoriesJson((Guid.NewGuid(), "A"), (Guid.NewGuid(), "B"));
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var vm = CreateVm(client);

        // act
        await vm.InitializeAsync();

        // assert
        Assert.True(vm.Loaded);
        Assert.Equal(2, vm.Categories.Count);
        Assert.Contains(vm.Categories, c => c.Name == "A");
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
        Assert.False(called);
    }

    [Fact]
    public async Task CreateAsync_Posts_SetsBusy_ResetsName_AndReloads()
    {
        // arrange
        var posted = false;
        var reloaded = false;
        var createdId = Guid.NewGuid();
        var client = CreateHttpClient(req =>
        {
            if (req.Method == HttpMethod.Post && req.RequestUri!.AbsolutePath == "/api/contact-categories")
            {
                posted = true;
                var createdJson = JsonSerializer.Serialize(new { Id = createdId, Name = "X", SymbolAttachmentId = (Guid?)null });
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(createdJson, Encoding.UTF8, "application/json")
                };
            }
            if (req.Method == HttpMethod.Get && req.RequestUri!.AbsolutePath == "/api/contact-categories")
            {
                reloaded = true;
                var json = CategoriesJson((createdId, "X"));
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };
            }
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var vm = CreateVm(client);
        await vm.InitializeAsync();
        vm.CreateName = "New";

        // act
        var before = vm.Busy;
        var task = vm.CreateAsync();
        Assert.True(vm.Busy); // busy set immediately
        await task;

        // assert
        Assert.True(posted);
        Assert.True(reloaded);
        Assert.False(vm.Busy);
        Assert.Equal(string.Empty, vm.CreateName);
        Assert.Single(vm.Categories);
    }

    [Fact]
    public async Task CreateAsync_SetsError_OnFailure()
    {
        // arrange
        var client = CreateHttpClient(req => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("bad")
        });
        var vm = CreateVm(client);
        await vm.InitializeAsync();
        vm.CreateName = "New";

        // act
        await vm.CreateAsync();

        // assert
        Assert.False(string.IsNullOrWhiteSpace(vm.Error));
        Assert.False(vm.Busy);
    }

    [Fact]
    public void GetRibbon_ContainsExpectedGroups()
    {
        var client = CreateHttpClient(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var vm = CreateVm(client);
        var loc = new DummyLocalizer();

        var groups = vm.GetRibbon(loc);
        Assert.Contains(groups, g => g.Title == "Ribbon_Group_Navigation");
        Assert.Contains(groups, g => g.Title == "Ribbon_Group_Actions");
        Assert.Contains(groups, g => g.Items.Any(i => i.Action == "Back"));
        Assert.Contains(groups, g => g.Items.Any(i => i.Action == "New"));
    }
}
