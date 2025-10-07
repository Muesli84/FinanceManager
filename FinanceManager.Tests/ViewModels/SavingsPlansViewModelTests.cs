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

public sealed class SavingsPlansViewModelTests
{
    private static HttpClient CreateHttpClient(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        return new HttpClient(new DelegateHandler(responder)) { BaseAddress = new Uri("http://localhost") };
    }

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

    private static IServiceProvider CreateSp(bool authenticated = true)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentUserService>(new TestCurrentUserService { IsAuthenticated = authenticated });
        // simple fake localizer returning key as value
        services.AddSingleton(typeof(IStringLocalizer<>), typeof(TestLocalizer<>));
        return services.BuildServiceProvider();
    }

    private static string PlansJson(params SavingsPlanDto[] plans)
    {
        return JsonSerializer.Serialize(plans);
    }

    private static string AnalysisJson(SavingsPlanAnalysisDto dto)
    {
        return JsonSerializer.Serialize(dto);
    }

    [Fact]
    public async Task InitializeAsync_LoadsPlans_AndAnalyses()
    {
        var plans = new[]
        {
            new SavingsPlanDto(Guid.NewGuid(), "P1", SavingsPlanType.Recurring, 1000m, new DateTime(2025,1,1), SavingsPlanInterval.Monthly, true, DateTime.UtcNow, null, null),
            new SavingsPlanDto(Guid.NewGuid(), "P2", SavingsPlanType.Open, null, null, null, true, DateTime.UtcNow, null, null)
        };

        var client = CreateHttpClient(req =>
        {
            if (req.Method == HttpMethod.Get && req.RequestUri!.AbsolutePath == "/api/savings-plans")
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(PlansJson(plans), Encoding.UTF8, "application/json") };
            }
            if (req.Method == HttpMethod.Get && req.RequestUri!.AbsolutePath.StartsWith($"/api/savings-plans/{plans[0].Id}/analysis"))
            {
                var dto = new SavingsPlanAnalysisDto(plans[0].Id, true, 1000m, new DateTime(2025,1,1), 300m, 50m, 14);
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(AnalysisJson(dto), Encoding.UTF8, "application/json") };
            }
            if (req.Method == HttpMethod.Get && req.RequestUri!.AbsolutePath.StartsWith($"/api/savings-plans/{plans[1].Id}/analysis"))
            {
                var dto = new SavingsPlanAnalysisDto(plans[1].Id, false, null, null, 0m, 0m, 0);
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(AnalysisJson(dto), Encoding.UTF8, "application/json") };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var vm = new SavingsPlansViewModel(CreateSp(), new TestHttpClientFactory(client));
        await vm.InitializeAsync();

        Assert.True(vm.Loaded);
        Assert.Equal(2, vm.Plans.Count);
    }

    [Fact]
    public async Task ToggleActiveOnly_Reloads()
    {
        int calls = 0;
        var client = CreateHttpClient(req =>
        {
            if (req.RequestUri!.AbsolutePath == "/api/savings-plans")
            {
                calls++;
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(PlansJson(), Encoding.UTF8, "application/json") };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var vm = new SavingsPlansViewModel(CreateSp(), new TestHttpClientFactory(client));

        await vm.InitializeAsync();
        Assert.Equal(1, calls);

        vm.ToggleActiveOnly();
        await Task.Delay(10);
        Assert.Equal(2, calls);
    }

    [Fact]
    public void GetStatusFlags_And_Label_Work()
    {
        var sp = CreateSp();
        var client = CreateHttpClient(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(PlansJson(), Encoding.UTF8, "application/json") });
        var vm = new SavingsPlansViewModel(sp, new TestHttpClientFactory(client));

        var planId = Guid.NewGuid();
        var plan = new SavingsPlanDto(planId, "P", SavingsPlanType.Recurring, 1000m, new DateTime(2025,1,1), SavingsPlanInterval.Monthly, true, DateTime.UtcNow, null, null);
        // inject analysis to internal dictionary via Initialize + reflection is overkill: simulate through private method? we can't access. Instead, call through status methods when no analysis: should be Normal -> Active label
        var loc = sp.GetRequiredService<IStringLocalizer<SavingsPlansViewModelTests>>();
        var label = vm.GetStatusLabel(loc, plan);
        Assert.False(string.IsNullOrWhiteSpace(label));
        var flags = vm.GetStatusFlags(plan);
        Assert.False(flags.Reachable);
        Assert.False(flags.Unreachable);
    }

    private sealed class TestLocalizer<T> : IStringLocalizer<T>
    {
        public LocalizedString this[string name]
        {
            get => new LocalizedString(name, name, resourceNotFound: false);
        }

        public LocalizedString this[string name, params object[] arguments]
        {
            get => new LocalizedString(name, string.Format(name, arguments), resourceNotFound: false);
        }

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures)
        {
            yield break;
        }

        public IStringLocalizer WithCulture(System.Globalization.CultureInfo culture)
        {
            return this;
        }
    }
}
