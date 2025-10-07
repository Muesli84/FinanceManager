using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FinanceManager.Application;
using FinanceManager.Shared.Dtos;
using FinanceManager.Web.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Xunit;

namespace FinanceManager.Tests.ViewModels;

public sealed class SetupProfileViewModelTests
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

    private static string ProfileJson(UserProfileSettingsDto dto) => JsonSerializer.Serialize(dto);

    [Fact]
    public async Task Initialize_Loads_Profile()
    {
        var dto = new UserProfileSettingsDto { PreferredLanguage = "de", TimeZoneId = "Europe/Berlin", HasAlphaVantageApiKey = true, ShareAlphaVantageApiKey = true };
        var client = CreateHttpClient(req =>
        {
            if (req.Method == HttpMethod.Get && req.RequestUri!.AbsolutePath == "/api/user/profile-settings")
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(ProfileJson(dto), Encoding.UTF8, "application/json") };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var vm = new SetupProfileViewModel(CreateSp(), new TestHttpClientFactory(client));
        await vm.InitializeAsync();

        Assert.False(vm.Loading);
        Assert.Equal("de", vm.Model.PreferredLanguage);
        Assert.True(vm.HasKey);
        Assert.True(vm.ShareKey);
        Assert.False(vm.Dirty);
    }

    [Fact]
    public async Task Save_Updates_State_And_Resets_Flags_On_Success()
    {
        var dto = new UserProfileSettingsDto { PreferredLanguage = "de", TimeZoneId = "Europe/Berlin", HasAlphaVantageApiKey = false, ShareAlphaVantageApiKey = false };
        bool putCalled = false;
        var client = CreateHttpClient(req =>
        {
            if (req.Method == HttpMethod.Get && req.RequestUri!.AbsolutePath == "/api/user/profile-settings")
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(ProfileJson(dto), Encoding.UTF8, "application/json") };
            }
            if (req.Method == HttpMethod.Put && req.RequestUri!.AbsolutePath == "/api/user/profile-settings")
            {
                putCalled = true;
                return new HttpResponseMessage(HttpStatusCode.OK);
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var vm = new SetupProfileViewModel(CreateSp(), new TestHttpClientFactory(client));
        await vm.InitializeAsync();

        vm.Model.PreferredLanguage = "en";
        vm.KeyInput = "abc";
        vm.ShareKey = true;
        vm.OnChanged();
        Assert.True(vm.Dirty);

        await vm.SaveAsync();
        Assert.True(putCalled);
        Assert.True(vm.SavedOk);
        Assert.False(vm.Dirty);
        Assert.Equal(string.Empty, vm.KeyInput);
    }

    [Fact]
    public async Task ClearKey_Sets_Dirty_And_Save_Sends_ClearFlag()
    {
        bool clearSent = false;
        var dto = new UserProfileSettingsDto { PreferredLanguage = "de", TimeZoneId = "Europe/Berlin", HasAlphaVantageApiKey = true, ShareAlphaVantageApiKey = false };
        var client = CreateHttpClient(req =>
        {
            if (req.Method == HttpMethod.Get && req.RequestUri!.AbsolutePath == "/api/user/profile-settings")
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(ProfileJson(dto), Encoding.UTF8, "application/json") };
            }
            if (req.Method == HttpMethod.Put && req.RequestUri!.AbsolutePath == "/api/user/profile-settings")
            {
                // naive check: content contains the clear flag
                var json = req.Content!.ReadAsStringAsync().Result;
                clearSent = json.Contains("ClearAlphaVantageApiKey");
                return new HttpResponseMessage(HttpStatusCode.OK);
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var vm = new SetupProfileViewModel(CreateSp(), new TestHttpClientFactory(client));
        await vm.InitializeAsync();

        vm.ClearKey();
        Assert.True(vm.Dirty);

        await vm.SaveAsync();
        Assert.True(clearSent);
        Assert.True(vm.SavedOk);
        Assert.False(vm.Dirty);
    }

    [Fact]
    public void SetDetected_Updates_Model_And_Dirty()
    {
        var vm = new SetupProfileViewModel(CreateSp(), new TestHttpClientFactory(CreateHttpClient(_ => new HttpResponseMessage(HttpStatusCode.NotFound))));
        vm.SetDetected("de-DE", "Europe/Berlin");
        Assert.Equal("de-DE", vm.Model.PreferredLanguage);
        Assert.Equal("Europe/Berlin", vm.Model.TimeZoneId);
        Assert.True(vm.Dirty);
    }
}
