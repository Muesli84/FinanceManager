using FinanceManager.Application;
using FinanceManager.Web.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Text;
using System.Text.Json;

namespace FinanceManager.Tests.ViewModels;

public sealed class SetupIpBlocksViewModelTests
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
        public bool IsAdmin { get; set; } = true;
    }

    private static IServiceProvider CreateSp()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentUserService>(new TestCurrentUserService());
        return services.BuildServiceProvider();
    }

    private static string ListJson(params object[] items) => JsonSerializer.Serialize(items);

    [Fact]
    public async Task Initialize_Loads_List_When_Admin()
    {
        var item = new { Id = Guid.NewGuid(), IpAddress = "1.2.3.4", IsBlocked = false };
        var client = CreateHttpClient(req =>
        {
            if (req.Method == HttpMethod.Get && req.RequestUri!.AbsolutePath == "/api/admin/ip-blocks")
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(ListJson(item), Encoding.UTF8, "application/json") };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var vm = new SetupIpBlocksViewModel(CreateSp(), new TestHttpClientFactory(client));
        await vm.InitializeAsync();

        Assert.Single(vm.Items);
        Assert.Equal("1.2.3.4", vm.Items[0].IpAddress);
    }

    [Fact]
    public async Task Create_Clears_Form_On_Success()
    {
        bool postCalled = false;
        var client = CreateHttpClient(req =>
        {
            if (req.Method == HttpMethod.Post && req.RequestUri!.AbsolutePath == "/api/admin/ip-blocks")
            {
                postCalled = true;
                var dtoJson = JsonSerializer.Serialize(new
                {
                    Id = Guid.NewGuid(),
                    IpAddress = "1.2.3.4",
                    IsBlocked = true,
                    BlockedAtUtc = (DateTime?)null,
                    BlockReason = "test",
                    UnknownUserFailedAttempts = 0,
                    UnknownUserLastFailedUtc = (DateTime?)null,
                    CreatedUtc = DateTime.UtcNow,
                    ModifiedUtc = (DateTime?)null
                });
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(dtoJson, Encoding.UTF8, "application/json")
                };
            }
            if (req.Method == HttpMethod.Get && req.RequestUri!.AbsolutePath == "/api/admin/ip-blocks")
            {
                // Keine Einträge nach Erstellung nötig für diesen Test
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(ListJson(), Encoding.UTF8, "application/json")
                };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var vm = new SetupIpBlocksViewModel(CreateSp(), new TestHttpClientFactory(client));
        vm.Ip = "1.2.3.4";
        vm.Reason = "test";

        await vm.CreateAsync();
        Assert.True(postCalled);
        Assert.Equal(string.Empty, vm.Ip);
        Assert.Null(vm.Reason);
        Assert.True(vm.BlockOnCreate);
    }
}
