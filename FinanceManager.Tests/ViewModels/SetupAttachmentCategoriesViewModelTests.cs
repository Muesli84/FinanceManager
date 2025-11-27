using FinanceManager.Application;
using FinanceManager.Web.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Text;
using System.Text.Json;

namespace FinanceManager.Tests.ViewModels;

public sealed class SetupAttachmentCategoriesViewModelTests
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

    private static string ListJson(params object[] items) => JsonSerializer.Serialize(items);

    [Fact]
    public async Task Initialize_Loads_And_Sorts()
    {
        var c1 = new { Id = Guid.NewGuid(), Name = "B", IsSystem = false, InUse = false };
        var c2 = new { Id = Guid.NewGuid(), Name = "A", IsSystem = false, InUse = false };
        var client = CreateHttpClient(req =>
        {
            if (req.Method == HttpMethod.Get && req.RequestUri!.AbsolutePath == "/api/attachments/categories")
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(ListJson(c1, c2), Encoding.UTF8, "application/json") };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var vm = new SetupAttachmentCategoriesViewModel(CreateSp(), new TestHttpClientFactory(client));
        await vm.InitializeAsync();

        Assert.Equal(2, vm.Items.Count);
        Assert.Equal(new[] { "A", "B" }, vm.Items.Select(x => x.Name).ToArray());
    }

    [Fact]
    public async Task AddAsync_Adds_And_Clears_And_Sets_ActionOk()
    {
        var created = new { Id = Guid.NewGuid(), Name = "Zeta", IsSystem = false, InUse = false };
        var client = CreateHttpClient(req =>
        {
            if (req.Method == HttpMethod.Get && req.RequestUri!.AbsolutePath == "/api/attachments/categories")
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(ListJson(), Encoding.UTF8, "application/json") };
            }
            if (req.Method == HttpMethod.Post && req.RequestUri!.AbsolutePath == "/api/attachments/categories")
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(created), Encoding.UTF8, "application/json") };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var vm = new SetupAttachmentCategoriesViewModel(CreateSp(), new TestHttpClientFactory(client));
        await vm.InitializeAsync();

        vm.NewName = "Zeta";
        await vm.AddAsync();

        Assert.True(vm.ActionOk);
        Assert.Equal(string.Empty, vm.NewName);
        Assert.Single(vm.Items);
        Assert.Equal("Zeta", vm.Items[0].Name);
    }

    [Fact]
    public async Task BeginEdit_And_SaveEdit_Updates_Item()
    {
        var id = Guid.NewGuid();
        var initial = new { Id = id, Name = "Old", IsSystem = false, InUse = false };
        var updated = new { Id = id, Name = "New", IsSystem = false, InUse = false };
        var client = CreateHttpClient(req =>
        {
            if (req.Method == HttpMethod.Get && req.RequestUri!.AbsolutePath == "/api/attachments/categories")
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(ListJson(initial), Encoding.UTF8, "application/json") };
            }
            if (req.Method == HttpMethod.Put && req.RequestUri!.AbsolutePath == $"/api/attachments/categories/{id}")
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(updated), Encoding.UTF8, "application/json") };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var vm = new SetupAttachmentCategoriesViewModel(CreateSp(), new TestHttpClientFactory(client));
        await vm.InitializeAsync();

        vm.BeginEdit(id, "Old");
        Assert.Equal(id, vm.EditId);
        vm.EditName = "New";
        await vm.SaveEditAsync();

        Assert.True(vm.ActionOk);
        Assert.Equal(Guid.Empty, vm.EditId);
        Assert.Single(vm.Items);
        Assert.Equal("New", vm.Items[0].Name);
    }

    [Fact]
    public async Task Delete_Removes_Item()
    {
        var id = Guid.NewGuid();
        var initial = new { Id = id, Name = "ToDelete", IsSystem = false, InUse = false };
        var client = CreateHttpClient(req =>
        {
            if (req.Method == HttpMethod.Get && req.RequestUri!.AbsolutePath == "/api/attachments/categories")
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(ListJson(initial), Encoding.UTF8, "application/json") };
            }
            if (req.Method == HttpMethod.Delete && req.RequestUri!.AbsolutePath == $"/api/attachments/categories/{id}")
            {
                return new HttpResponseMessage(HttpStatusCode.OK);
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var vm = new SetupAttachmentCategoriesViewModel(CreateSp(), new TestHttpClientFactory(client));
        await vm.InitializeAsync();

        Assert.Single(vm.Items);
        await vm.DeleteAsync(id);
        Assert.True(vm.ActionOk);
        Assert.Empty(vm.Items);
    }
}
