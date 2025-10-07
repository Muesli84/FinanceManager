using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FinanceManager.Application;
using FinanceManager.Shared.Dtos;
using FinanceManager.Web.ViewModels;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Xunit;

namespace FinanceManager.Tests.Web.ViewModels;

public sealed class ContactsViewModelTests
{
    private sealed class TestCurrentUserService : ICurrentUserService
    {
        public Guid UserId { get; set; } = Guid.NewGuid();
        public string? PreferredLanguage { get; set; }
        public bool IsAuthenticated { get; set; }
        public bool IsAdmin { get; set; }
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public StubHttpClientFactory(HttpMessageHandler handler) => _handler = handler;
        public HttpClient CreateClient(string name) => new HttpClient(_handler) { BaseAddress = new Uri("http://localhost") };
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_responder(request));
    }

    private sealed class PassthroughLocalizer : IStringLocalizer
    {
        public LocalizedString this[string name]
            => new LocalizedString(name, name, resourceNotFound: false);
        public LocalizedString this[string name, params object[] arguments]
            => new LocalizedString(name, string.Format(name, arguments), resourceNotFound: false);
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => Array.Empty<LocalizedString>();
        public IStringLocalizer WithCulture(System.Globalization.CultureInfo culture) => this;
    }

    private static (ContactsViewModel vm, TestCurrentUserService current) CreateVm(Func<HttpRequestMessage, HttpResponseMessage> responder, bool isAuthenticated)
    {
        var services = new ServiceCollection();
        var current = new TestCurrentUserService { IsAuthenticated = isAuthenticated };
        services.AddSingleton<ICurrentUserService>(current);
        var sp = services.BuildServiceProvider();
        var handler = new StubHandler(responder);
        var factory = new StubHttpClientFactory(handler);
        var vm = new ContactsViewModel(sp, factory);
        return (vm, current);
    }

    [Fact]
    public async Task InitializeAsync_ShouldRequestAuth_WhenNotAuthenticated()
    {
        var (vm, _) = CreateVm(_ => new HttpResponseMessage(HttpStatusCode.OK), isAuthenticated: false);
        string? requestedReturn = null;
        vm.AuthenticationRequired += (_, ret) => requestedReturn = ret;

        await vm.InitializeAsync();

        requestedReturn.Should().BeNull();
        vm.Loaded.Should().BeFalse();
    }

    [Fact]
    public async Task InitializeAsync_ShouldLoadCategories_And_FirstPage_WhenAuthenticated()
    {
        var categories = new[] { new { Id = Guid.NewGuid(), Name = "Friends" } };
        var contactId = Guid.NewGuid();
        var contacts = new[] { new { Id = contactId, Name = "Alice", Type = ContactType.Person, CategoryId = (Guid?)categories[0].Id, Description = (string?)null, IsPaymentIntermediary = false } };

        var (vm, _) = CreateVm(req =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("/api/contact-categories"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(categories), Encoding.UTF8, "application/json")
                };
            }
            if (req.RequestUri!.AbsolutePath.EndsWith("/api/contacts"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(contacts), Encoding.UTF8, "application/json")
                };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }, isAuthenticated: true);

        await vm.InitializeAsync();

        vm.Loaded.Should().BeTrue();
        vm.Contacts.Should().HaveCount(1);
        vm.Contacts[0].Name.Should().Be("Alice");
        vm.Contacts[0].CategoryName.Should().Be("Friends");
    }

    [Fact]
    public async Task LoadMoreAsync_ShouldPaginate_And_SetAllLoaded()
    {
        // First page returns 50, second page returns 10
        int callCount = 0;
        var firstPage = Enumerable.Range(0, 50).Select(i => new { Id = Guid.NewGuid(), Name = $"N{i}", Type = ContactType.Person, CategoryId = (Guid?)null, Description = (string?)null, IsPaymentIntermediary = false }).ToArray();
        var secondPage = Enumerable.Range(0, 10).Select(i => new { Id = Guid.NewGuid(), Name = $"M{i}", Type = ContactType.Person, CategoryId = (Guid?)null, Description = (string?)null, IsPaymentIntermediary = false }).ToArray();

        var (vm, _) = CreateVm(req =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("/api/contact-categories"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("[]", Encoding.UTF8, "application/json")
                };
            }
            if (req.RequestUri!.AbsolutePath.EndsWith("/api/contacts"))
            {
                callCount++;
                var payload = callCount == 1 ? firstPage : secondPage;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
                };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }, isAuthenticated: true);

        await vm.InitializeAsync(); // loads first page
        vm.Contacts.Should().HaveCount(50);
        vm.AllLoaded.Should().BeFalse();

        await vm.LoadMoreAsync();
        vm.Contacts.Should().HaveCount(60);
        vm.AllLoaded.Should().BeTrue();
    }

    [Fact]
    public async Task SetFilterAsync_ShouldResetAndReload_AndRibbonIncludesClear()
    {
        // Categories empty; contacts filtered call returns different set
        var (vm, _) = CreateVm(req =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("/api/contact-categories"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("[]", Encoding.UTF8, "application/json")
                };
            }
            if (req.RequestUri!.AbsolutePath.EndsWith("/api/contacts"))
            {
                // if q present -> return two items, else one
                var query = System.Web.HttpUtility.ParseQueryString(req.RequestUri.Query);
                var hasQ = !string.IsNullOrWhiteSpace(query.Get("q"));
                var items = hasQ
                    ? new[] { new { Id = Guid.NewGuid(), Name = "Ax", Type = ContactType.Person, CategoryId = (Guid?)null, Description = (string?)null, IsPaymentIntermediary = false }, new { Id = Guid.NewGuid(), Name = "Ay", Type = ContactType.Person, CategoryId = (Guid?)null, Description = (string?)null, IsPaymentIntermediary = false } }
                    : new[] { new { Id = Guid.NewGuid(), Name = "B", Type = ContactType.Person, CategoryId = (Guid?)null, Description = (string?)null, IsPaymentIntermediary = false } };
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(items), Encoding.UTF8, "application/json")
                };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }, isAuthenticated: true);

        await vm.InitializeAsync();
        vm.Contacts.Should().HaveCount(1);

        await vm.SetFilterAsync("A");
        vm.Contacts.Should().HaveCount(2);
        vm.AllLoaded.Should().BeTrue(); // because page size > returned

        var ribbon = vm.GetRibbon(new PassthroughLocalizer());
        ribbon.Should().HaveCount(1);
        var items = ribbon[0].Items;
        items.Any(i => i.Action == "ClearFilter").Should().BeTrue();
    }
}
