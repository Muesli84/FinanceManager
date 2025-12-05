using FinanceManager.Application;
using FinanceManager.Web.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Text;
using System.Text.Json;

namespace FinanceManager.Tests.ViewModels;

public sealed class SetupBackupsViewModelTests
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

    private static FinanceManager.Shared.IApiClient CreateApiClient(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var http = CreateHttpClient(responder);
        return new FinanceManager.Shared.ApiClient(http);
    }

    [Fact]
    public async Task Initialize_Loads_List()
    {
        var item = new { Id = Guid.NewGuid(), CreatedUtc = DateTime.UtcNow, FileName = "b1.zip", SizeBytes = 123L, Source = "Manual" };
        var api = CreateApiClient(req =>
        {
            if (req.Method == HttpMethod.Get && req.RequestUri!.AbsolutePath == "/api/setup/backups")
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(ListJson(item), Encoding.UTF8, "application/json") };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var vm = new SetupBackupsViewModel(CreateSp(), api);
        await vm.InitializeAsync();

        Assert.NotNull(vm.Backups);
        Assert.Single(vm.Backups);
        Assert.Equal("b1.zip", vm.Backups![0].FileName);
    }

    [Fact]
    public async Task Create_Inserts_Item_And_Delete_Removes()
    {
        var created = new { Id = Guid.NewGuid(), CreatedUtc = DateTime.UtcNow, FileName = "b2.zip", SizeBytes = 456L, Source = "Manual" };
        var api = CreateApiClient(req =>
        {
            if (req.Method == HttpMethod.Get && req.RequestUri!.AbsolutePath == "/api/setup/backups")
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(ListJson(), Encoding.UTF8, "application/json") };
            }
            if (req.Method == HttpMethod.Post && req.RequestUri!.AbsolutePath == "/api/setup/backups")
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(created), Encoding.UTF8, "application/json") };
            }
            if (req.Method == HttpMethod.Delete && req.RequestUri!.AbsolutePath == $"/api/setup/backups/{created.Id}")
            {
                return new HttpResponseMessage(HttpStatusCode.OK);
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var vm = new SetupBackupsViewModel(CreateSp(), api);
        await vm.InitializeAsync();

        await vm.CreateAsync();
        Assert.Single(vm.Backups!);
        Assert.Equal("b2.zip", vm.Backups![0].FileName);

        await vm.DeleteAsync(created.Id);
        Assert.Empty(vm.Backups!);
    }

    [Fact]
    public async Task StartApply_Sets_Flag_On_Success()
    {
        var id = Guid.NewGuid();
        var api = CreateApiClient(req =>
        {
            if (req.Method == HttpMethod.Get && req.RequestUri!.AbsolutePath == "/api/setup/backups")
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(ListJson(), Encoding.UTF8, "application/json") };
            }
            if (req.Method == HttpMethod.Post && req.RequestUri!.AbsolutePath == $"/api/setup/backups/{id}/apply/start")
            {
                var status = new FinanceManager.Shared.Dtos.Admin.BackupRestoreStatusDto(true, 0, 1, null, null, 0, 0, null);
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(status), Encoding.UTF8, "application/json") };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var vm = new SetupBackupsViewModel(CreateSp(), api);
        await vm.InitializeAsync();

        await vm.StartApplyAsync(id);
        Assert.True(vm.HasActiveRestore);
    }
}
