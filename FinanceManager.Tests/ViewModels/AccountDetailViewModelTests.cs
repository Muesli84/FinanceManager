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

public sealed class AccountDetailViewModelTests
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

    private static (AccountDetailViewModel vm, TestCurrentUserService user) CreateVm(HttpClient client)
    {
        var services = new ServiceCollection();
        var currentUser = new TestCurrentUserService { IsAuthenticated = true };
        services.AddSingleton<ICurrentUserService>(currentUser);
        var sp = services.BuildServiceProvider();
        var factory = new TestHttpClientFactory(client);
        var vm = new AccountDetailViewModel(sp, factory);
        return (vm, currentUser);
    }

    private static string ContactsJson(params (Guid id, string name)[] items)
        => JsonSerializer.Serialize(items.Select(c => new { Id = c.id, Name = c.name }).ToArray());

    private static string AccountJson(Guid id, string name, int type, string? iban, Guid? bankContactId)
        => JsonSerializer.Serialize(new { Id = id, Name = name, Type = type, Iban = iban, CurrentBalance = 0m, BankContactId = bankContactId });

    [Fact]
    public async Task Initialize_LoadsContacts_AndExistingAccount()
    {
        // arrange
        var accountId = Guid.NewGuid();
        var client = CreateHttpClient(req =>
        {
            if (req.Method == HttpMethod.Get && req.RequestUri!.PathAndQuery.StartsWith("/api/contacts"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(ContactsJson((Guid.NewGuid(), "Bank A"), (Guid.NewGuid(), "Bank B")), Encoding.UTF8, "application/json")
                };
            }
            if (req.Method == HttpMethod.Get && req.RequestUri!.AbsolutePath == $"/api/accounts/{accountId}")
            {
                var json = AccountJson(accountId, "My Account", (int)AccountType.Giro, "DE00", Guid.NewGuid());
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var (vm, _) = CreateVm(client);
        vm.ForAccount(accountId);

        // act
        await vm.InitializeAsync();

        // assert
        Assert.True(vm.Loaded);
        Assert.NotEmpty(vm.BankContacts);
        Assert.Equal("My Account", vm.Name);
        Assert.Equal(AccountType.Giro, vm.Type);
        Assert.Equal("DE00", vm.Iban);
        Assert.False(vm.IsNew);
        Assert.True(vm.ShowCharts);
    }

    [Fact]
    public async Task Initialize_NewAccount_LoadsOnlyContacts()
    {
        // arrange
        var calls = 0;
        var client = CreateHttpClient(req =>
        {
            if (req.RequestUri!.PathAndQuery.StartsWith("/api/contacts"))
            {
                calls++;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(ContactsJson((Guid.NewGuid(), "Bank A")), Encoding.UTF8, "application/json")
                };
            }
            if (req.RequestUri!.AbsolutePath.StartsWith("/api/accounts/"))
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var (vm, _) = CreateVm(client);
        vm.ForAccount(null);

        // act
        await vm.InitializeAsync();

        // assert
        Assert.True(vm.Loaded);
        Assert.Single(vm.BankContacts);
        Assert.True(vm.IsNew);
        Assert.False(vm.ShowCharts);
    }

    [Fact]
    public async Task SaveAsync_New_PostsAndReturnsId()
    {
        // arrange
        var createdId = Guid.NewGuid();
        var client = CreateHttpClient(req =>
        {
            if (req.Method == HttpMethod.Post && req.RequestUri!.AbsolutePath == "/api/accounts")
            {
                var json = AccountJson(createdId, "Created", (int)AccountType.Savings, null, null);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };
            }
            if (req.Method == HttpMethod.Get && req.RequestUri!.PathAndQuery.StartsWith("/api/contacts"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(ContactsJson(), Encoding.UTF8, "application/json")
                };
            }
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var (vm, _) = CreateVm(client);
        vm.ForAccount(null);
        vm.Name = "New";
        vm.Type = AccountType.Savings;

        // act
        await vm.InitializeAsync();
        var id = await vm.SaveAsync();

        // assert
        Assert.True(id.HasValue);
        Assert.Equal(createdId, id.Value);
        Assert.False(vm.IsNew);
    }

    [Fact]
    public async Task SaveAsync_Update_PutsAndKeepsId()
    {
        // arrange
        var accountId = Guid.NewGuid();
        var client = CreateHttpClient(req =>
        {
            if (req.Method == HttpMethod.Put && req.RequestUri!.AbsolutePath == $"/api/accounts/{accountId}")
            {
                return new HttpResponseMessage(HttpStatusCode.OK);
            }
            if (req.Method == HttpMethod.Get && req.RequestUri!.PathAndQuery.StartsWith("/api/contacts"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(ContactsJson(), Encoding.UTF8, "application/json")
                };
            }
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var (vm, _) = CreateVm(client);
        vm.ForAccount(accountId);
        vm.Name = "Existing";
        vm.Type = AccountType.Giro;

        await vm.InitializeAsync();
        var id = await vm.SaveAsync();

        Assert.Null(id);
        Assert.False(vm.IsNew);
        Assert.Null(vm.Error);
    }

    [Fact]
    public async Task DeleteAsync_Success_ClearsError()
    {
        var accountId = Guid.NewGuid();
        var client = CreateHttpClient(req => new HttpResponseMessage(HttpStatusCode.OK));
        var (vm, _) = CreateVm(client);
        vm.ForAccount(accountId);
        await vm.DeleteAsync();
        Assert.Null(vm.Error);
    }

    [Fact]
    public void GetRibbon_ContainsExpectedGroups()
    {
        // arrange
        var client = CreateHttpClient(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var (vm, _) = CreateVm(client);
        var loc = new DummyLocalizer();

        // New account: no related/delete, save disabled when name empty
        vm.ForAccount(null);
        var groupsNew = vm.GetRibbon(loc);
        Assert.Contains(groupsNew, g => g.Title == "Ribbon_Group_Navigation");
        Assert.Contains(groupsNew, g => g.Title == "Ribbon_Group_Edit");
        Assert.DoesNotContain(groupsNew, g => g.Title == "Ribbon_Group_Related");

        // Existing account: related + delete present
        vm.ForAccount(Guid.NewGuid());
        vm.Name = "Acc";
        var groupsExisting = vm.GetRibbon(loc);
        Assert.Contains(groupsExisting, g => g.Title == "Ribbon_Group_Related");
    }
}
