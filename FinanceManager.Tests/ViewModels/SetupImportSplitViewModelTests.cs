using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FinanceManager.Application;
using FinanceManager.Shared.Dtos;
using FinanceManager.Web.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FinanceManager.Tests.ViewModels;

public sealed class SetupImportSplitViewModelTests
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

    private static string SettingsJson(ImportSplitSettingsDto dto) => JsonSerializer.Serialize(dto);

    [Fact]
    public async Task Initialize_Loads_Settings()
    {
        var dto = new ImportSplitSettingsDto { Mode = ImportSplitMode.Monthly, MaxEntriesPerDraft = 200, MonthlySplitThreshold = 250, MinEntriesPerDraft = 5 };
        var client = CreateHttpClient(req =>
        {
            if (req.Method == HttpMethod.Get && req.RequestUri!.AbsolutePath == "/api/user/import-split-settings")
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(SettingsJson(dto), Encoding.UTF8, "application/json") };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var vm = new SetupImportSplitViewModel(CreateSp(), new TestHttpClientFactory(client));
        await vm.InitializeAsync();

        Assert.False(vm.Loading);
        Assert.NotNull(vm.Model);
        Assert.Equal(ImportSplitMode.Monthly, vm.Model!.Mode);
    }

    [Fact]
    public async Task Validate_Disallows_Invalid_Combinations()
    {
        var client = CreateHttpClient(req => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(SettingsJson(new ImportSplitSettingsDto()), Encoding.UTF8, "application/json")
        });
        var vm = new SetupImportSplitViewModel(CreateSp(), new TestHttpClientFactory(client));
        await vm.InitializeAsync();

        vm.Model!.MaxEntriesPerDraft = 10;
        vm.Validate();
        Assert.True(vm.HasValidationError);

        vm.Model!.MaxEntriesPerDraft = 100;
        vm.Model!.Mode = ImportSplitMode.Monthly;
        vm.Model!.MinEntriesPerDraft = 0;
        vm.Validate();
        Assert.True(vm.HasValidationError);

        vm.Model!.MinEntriesPerDraft = 10;
        vm.Model!.MonthlySplitThreshold = 100; // equal to max => ok
        vm.Model!.Mode = ImportSplitMode.MonthlyOrFixed;
        vm.Validate();
        Assert.False(vm.HasValidationError);
        vm.Model!.MonthlySplitThreshold = 50; // less than max => error
        vm.Validate();
        Assert.True(vm.HasValidationError);
    }

    [Fact]
    public async Task Save_Sets_SavedOk_And_Resets_Dirty()
    {
        var dto = new ImportSplitSettingsDto();
        bool putCalled = false;
        var client = CreateHttpClient(req =>
        {
            if (req.Method == HttpMethod.Get && req.RequestUri!.AbsolutePath == "/api/user/import-split-settings")
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(SettingsJson(dto), Encoding.UTF8, "application/json") };
            }
            if (req.Method == HttpMethod.Put && req.RequestUri!.AbsolutePath == "/api/user/import-split-settings")
            {
                putCalled = true;
                return new HttpResponseMessage(HttpStatusCode.OK);
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var vm = new SetupImportSplitViewModel(CreateSp(), new TestHttpClientFactory(client));
        await vm.InitializeAsync();

        vm.Model!.MaxEntriesPerDraft = 300;
        vm.OnModeChanged();
        Assert.True(vm.Dirty);

        await vm.SaveAsync();
        Assert.True(putCalled);
        Assert.True(vm.SavedOk);
        Assert.False(vm.Dirty);
    }
}
