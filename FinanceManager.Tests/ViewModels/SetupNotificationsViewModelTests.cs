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

public sealed class SetupNotificationsViewModelTests
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

    private static string SettingsJson(NotificationSettingsDto dto) => JsonSerializer.Serialize(dto);
    private static string ArrayJson(string[] items) => JsonSerializer.Serialize(items);

    [Fact]
    public async Task Initialize_Loads_Settings_And_Subdivisions()
    {
        var dto = new NotificationSettingsDto { MonthlyReminderEnabled = true, MonthlyReminderHour = 8, MonthlyReminderMinute = 30, HolidayProvider = "NagerDate", HolidayCountryCode = "DE" };
        var subs = new[] { "BW", "BY" };
        var client = CreateHttpClient(req =>
        {
            if (req.Method == HttpMethod.Get && req.RequestUri!.AbsolutePath == "/api/user/notification-settings")
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(SettingsJson(dto), Encoding.UTF8, "application/json") };
            }
            if (req.Method == HttpMethod.Get && req.RequestUri!.AbsolutePath == "/api/meta/holiday-subdivisions")
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(ArrayJson(subs), Encoding.UTF8, "application/json") };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var vm = new SetupNotificationsViewModel(CreateSp(), new TestHttpClientFactory(client));
        await vm.InitializeAsync();

        Assert.False(vm.Loading);
        Assert.True(vm.Model.MonthlyReminderEnabled);
        Assert.Equal(8, vm.Hour);
        Assert.Equal(30, vm.Minute);
        Assert.NotNull(vm.Subdivisions);
        Assert.Contains("BW", vm.Subdivisions);
    }

    [Fact]
    public async Task ProviderChange_Memory_Clears_Subdivision_And_Dirty()
    {
        var dto = new NotificationSettingsDto { HolidayProvider = "NagerDate", HolidayCountryCode = "DE", HolidaySubdivisionCode = "BW" };
        var client = CreateHttpClient(req =>
        {
            if (req.Method == HttpMethod.Get && req.RequestUri!.AbsolutePath == "/api/user/notification-settings")
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(SettingsJson(dto), Encoding.UTF8, "application/json") };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var vm = new SetupNotificationsViewModel(CreateSp(), new TestHttpClientFactory(client));
        await vm.InitializeAsync();

        vm.Model.HolidayProvider = "Memory";
        await vm.OnProviderChanged();
        Assert.Null(vm.Model.HolidaySubdivisionCode);
        Assert.True(vm.Dirty);
    }

    [Fact]
    public async Task Save_Sets_SavedOk_And_Resets_Dirty()
    {
        var dto = new NotificationSettingsDto { MonthlyReminderEnabled = false, MonthlyReminderHour = 9, MonthlyReminderMinute = 0, HolidayProvider = "Memory" };
        bool putCalled = false;
        var client = CreateHttpClient(req =>
        {
            if (req.Method == HttpMethod.Get && req.RequestUri!.AbsolutePath == "/api/user/notification-settings")
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(SettingsJson(dto), Encoding.UTF8, "application/json") };
            }
            if (req.Method == HttpMethod.Put && req.RequestUri!.AbsolutePath == "/api/user/notification-settings")
            {
                putCalled = true;
                return new HttpResponseMessage(HttpStatusCode.OK);
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var vm = new SetupNotificationsViewModel(CreateSp(), new TestHttpClientFactory(client));
        await vm.InitializeAsync();

        vm.Model.MonthlyReminderEnabled = true;
        vm.Hour = 10;
        vm.Minute = 15;
        vm.OnChanged();
        Assert.True(vm.Dirty);

        await vm.SaveAsync();
        Assert.True(putCalled);
        Assert.True(vm.SavedOk);
        Assert.False(vm.Dirty);
    }
}
