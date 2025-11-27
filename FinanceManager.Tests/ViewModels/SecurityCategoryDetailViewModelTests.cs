using FinanceManager.Application;
using FinanceManager.Web.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using System.Net;
using System.Text;
using System.Text.Json;

namespace FinanceManager.Tests.ViewModels;

public sealed class SecurityCategoryDetailViewModelTests
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

    private static IServiceProvider CreateSp()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentUserService>(new TestCurrentUserService());
        return services.BuildServiceProvider();
    }

    private static string CatJson(object o) => JsonSerializer.Serialize(o);

    [Fact]
    public async Task Initialize_Edit_Loads_Model()
    {
        var id = Guid.NewGuid();
        var client = CreateHttpClient(req =>
        {
            if (req.Method == HttpMethod.Get && req.RequestUri!.AbsolutePath == $"/api/security-categories/{id}")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(CatJson(new { Id = id, Name = "Cat1" }), Encoding.UTF8, "application/json")
                };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var vm = new SecurityCategoryDetailViewModel(CreateSp(), new TestHttpClientFactory(client));
        await vm.InitializeAsync(id);

        Assert.True(vm.IsEdit);
        Assert.True(vm.Loaded);
        Assert.Equal("Cat1", vm.Model.Name);
        Assert.Null(vm.Error);
    }

    [Fact]
    public async Task Initialize_Edit_NotFound_Sets_Error()
    {
        var id = Guid.NewGuid();
        var client = CreateHttpClient(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var vm = new SecurityCategoryDetailViewModel(CreateSp(), new TestHttpClientFactory(client));
        await vm.InitializeAsync(id);
        Assert.True(vm.IsEdit);
        Assert.True(vm.Loaded);
        Assert.Equal("Err_NotFound", vm.Error);
    }

    [Fact]
    public async Task Save_New_Success_And_Fail()
    {
        bool postCalled = false;
        var client = CreateHttpClient(req =>
        {
            if (req.Method == HttpMethod.Post && req.RequestUri!.AbsolutePath == "/api/security-categories")
            {
                postCalled = true;
                return new HttpResponseMessage(HttpStatusCode.OK);
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var vm = new SecurityCategoryDetailViewModel(CreateSp(), new TestHttpClientFactory(client));
        await vm.InitializeAsync(null);
        vm.Model.Name = "NewCat";
        var ok = await vm.SaveAsync();
        Assert.True(ok);
        Assert.True(postCalled);
        Assert.Null(vm.Error);

        // Fail
        var clientFail = CreateHttpClient(req =>
        {
            if (req.Method == HttpMethod.Post && req.RequestUri!.AbsolutePath == "/api/security-categories")
            {
                return new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("bad", Encoding.UTF8, "text/plain") };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var vmFail = new SecurityCategoryDetailViewModel(CreateSp(), new TestHttpClientFactory(clientFail));
        await vmFail.InitializeAsync(null);
        vmFail.Model.Name = "X";
        var ok2 = await vmFail.SaveAsync();
        Assert.False(ok2);
        Assert.Equal("bad", vmFail.Error);
    }

    [Fact]
    public async Task Save_Edit_Success_And_Fail()
    {
        var id = Guid.NewGuid();
        var client = CreateHttpClient(req =>
        {
            if (req.Method == HttpMethod.Get && req.RequestUri!.AbsolutePath == $"/api/security-categories/{id}")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(CatJson(new { Id = id, Name = "Cat" }), Encoding.UTF8, "application/json")
                };
            }
            if (req.Method == HttpMethod.Put && req.RequestUri!.AbsolutePath == $"/api/security-categories/{id}")
            {
                return new HttpResponseMessage(HttpStatusCode.OK);
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var vm = new SecurityCategoryDetailViewModel(CreateSp(), new TestHttpClientFactory(client));
        await vm.InitializeAsync(id);
        vm.Model.Name = "Updated";
        var ok = await vm.SaveAsync();
        Assert.True(ok);
        Assert.Null(vm.Error);

        var clientFail = CreateHttpClient(req =>
        {
            if (req.Method == HttpMethod.Get && req.RequestUri!.AbsolutePath == $"/api/security-categories/{id}")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(CatJson(new { Id = id, Name = "Cat" }), Encoding.UTF8, "application/json")
                };
            }
            if (req.Method == HttpMethod.Put && req.RequestUri!.AbsolutePath == $"/api/security-categories/{id}")
            {
                return new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("oops", Encoding.UTF8, "text/plain") };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var vm2 = new SecurityCategoryDetailViewModel(CreateSp(), new TestHttpClientFactory(clientFail));
        await vm2.InitializeAsync(id);
        vm2.Model.Name = "Updated";
        var ok2 = await vm2.SaveAsync();
        Assert.False(ok2);
        Assert.Equal("oops", vm2.Error);
    }

    [Fact]
    public async Task Delete_Success_And_Fail()
    {
        var id = Guid.NewGuid();
        var client = CreateHttpClient(req =>
        {
            if (req.Method == HttpMethod.Get && req.RequestUri!.AbsolutePath == $"/api/security-categories/{id}")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(CatJson(new { Id = id, Name = "Cat" }), Encoding.UTF8, "application/json")
                };
            }
            if (req.Method == HttpMethod.Delete && req.RequestUri!.AbsolutePath == $"/api/security-categories/{id}")
            {
                return new HttpResponseMessage(HttpStatusCode.OK);
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var vm = new SecurityCategoryDetailViewModel(CreateSp(), new TestHttpClientFactory(client));
        await vm.InitializeAsync(id);
        var ok = await vm.DeleteAsync();
        Assert.True(ok);

        var clientFail = CreateHttpClient(req =>
        {
            if (req.Method == HttpMethod.Get && req.RequestUri!.AbsolutePath == $"/api/security-categories/{id}")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(CatJson(new { Id = id, Name = "Cat" }), Encoding.UTF8, "application/json")
                };
            }
            if (req.Method == HttpMethod.Delete && req.RequestUri!.AbsolutePath == $"/api/security-categories/{id}")
            {
                return new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("bad", Encoding.UTF8, "text/plain") };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var vm2 = new SecurityCategoryDetailViewModel(CreateSp(), new TestHttpClientFactory(clientFail));
        await vm2.InitializeAsync(id);
        var ok2 = await vm2.DeleteAsync();
        Assert.False(ok2);
        Assert.Equal("bad", vm2.Error);
    }

    [Fact]
    public void Ribbon_Disables_Save_When_Name_Short()
    {
        var vm = new SecurityCategoryDetailViewModel(CreateSp(), new TestHttpClientFactory(CreateHttpClient(_ => new HttpResponseMessage(HttpStatusCode.NotFound))));
        var loc = new TestLocalizer<SecurityCategoryDetailViewModelTests>();
        var groups = vm.GetRibbon(loc);
        var edit = groups.First(g => g.Title == "Ribbon_Group_Edit");
        var save = edit.Items.First(i => i.Action == "Save");
        Assert.True(save.Disabled);

        vm.Model.Name = "OK";
        groups = vm.GetRibbon(loc);
        save = groups.First(g => g.Title == "Ribbon_Group_Edit").Items.First(i => i.Action == "Save");
        Assert.False(save.Disabled);
    }

    private sealed class TestLocalizer<T> : IStringLocalizer<T>
    {
        public LocalizedString this[string name] => new(name, name);
        public LocalizedString this[string name, params object[] arguments] => new(name, string.Format(name, arguments));
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) { yield break; }
        public IStringLocalizer WithCulture(System.Globalization.CultureInfo culture) => this;
    }
}
