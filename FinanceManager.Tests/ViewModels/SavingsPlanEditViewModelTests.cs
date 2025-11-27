using FinanceManager.Application;
using FinanceManager.Web.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using System.Net;
using System.Text;
using System.Text.Json;

namespace FinanceManager.Tests.ViewModels;

public sealed class SavingsPlanEditViewModelTests
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

    private sealed class TestLocalizer<T> : IStringLocalizer<T>
    {
        public LocalizedString this[string name] => new(name, name);
        public LocalizedString this[string name, params object[] arguments] => new(name, string.Format(name, arguments));
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) { yield break; }
        public IStringLocalizer WithCulture(System.Globalization.CultureInfo culture) => this;
    }

    private static IServiceProvider CreateSp()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentUserService>(new TestCurrentUserService());
        return services.BuildServiceProvider();
    }

    private static string PlanJson(SavingsPlanDto dto) => JsonSerializer.Serialize(dto);
    private static string AnalysisJson(SavingsPlanAnalysisDto dto) => JsonSerializer.Serialize(dto);
    private static string CategoriesJson(params SavingsPlanCategoryDto[] cats) => JsonSerializer.Serialize(cats.ToList());

    [Fact]
    public async Task InitializeAsync_Loads_Edit()
    {
        var id = Guid.NewGuid();
        var plan = new SavingsPlanDto(id, "Plan A", SavingsPlanType.Recurring, 1000m, new DateTime(2026, 1, 1), SavingsPlanInterval.Monthly, true, DateTime.UtcNow, null, null);
        var analysis = new SavingsPlanAnalysisDto(id, true, 1000m, new DateTime(2026, 1, 1), 200m, 50m, 20);
        var cats = new[] { new SavingsPlanCategoryDto { Id = Guid.NewGuid(), Name = "Cat1" } };

        var client = CreateHttpClient(req =>
        {
            if (req.Method == HttpMethod.Get && req.RequestUri!.AbsolutePath == $"/api/savings-plans/{id}")
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(PlanJson(plan), Encoding.UTF8, "application/json") };
            }
            if (req.Method == HttpMethod.Get && req.RequestUri!.AbsolutePath == $"/api/savings-plans/{id}/analysis")
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(AnalysisJson(analysis), Encoding.UTF8, "application/json") };
            }
            if (req.Method == HttpMethod.Get && req.RequestUri!.AbsolutePath == "/api/savings-plan-categories")
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(CategoriesJson(cats), Encoding.UTF8, "application/json") };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var vm = new SavingsPlanEditViewModel(CreateSp(), new TestHttpClientFactory(client));
        await vm.InitializeAsync(id, backNav: null, draftId: null, entryId: null, prefillName: null);

        Assert.True(vm.IsEdit);
        Assert.True(vm.Loaded);
        Assert.Equal("Plan A", vm.Model.Name);
        Assert.NotNull(vm.Analysis);
        Assert.Single(vm.Categories);
    }

    [Fact]
    public async Task InitializeAsync_New_Prefill_Sets_Name()
    {
        var client = CreateHttpClient(req =>
        {
            if (req.Method == HttpMethod.Get && req.RequestUri!.AbsolutePath == "/api/savings-plan-categories")
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(CategoriesJson(), Encoding.UTF8, "application/json") };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var vm = new SavingsPlanEditViewModel(CreateSp(), new TestHttpClientFactory(client));

        await vm.InitializeAsync(null, backNav: null, draftId: null, entryId: null, prefillName: "Hello");

        Assert.False(vm.IsEdit);
        Assert.Equal("Hello", vm.Model.Name);
        Assert.True(vm.Loaded);
    }

    [Fact]
    public async Task SaveAsync_Edit_Success()
    {
        var id = Guid.NewGuid();
        var plan = new SavingsPlanDto(id, "Plan A", SavingsPlanType.Recurring, null, null, null, true, DateTime.UtcNow, null, null);
        var client = CreateHttpClient(req =>
        {
            if (req.Method == HttpMethod.Get && req.RequestUri!.AbsolutePath == $"/api/savings-plans/{id}")
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(PlanJson(plan), Encoding.UTF8, "application/json") };
            }
            if (req.Method == HttpMethod.Get && req.RequestUri!.AbsolutePath == "/api/savings-plan-categories")
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(CategoriesJson(), Encoding.UTF8, "application/json") };
            }
            if (req.Method == HttpMethod.Put && req.RequestUri!.AbsolutePath == $"/api/savings-plans/{id}")
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(PlanJson(plan), Encoding.UTF8, "application/json") };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var vm = new SavingsPlanEditViewModel(CreateSp(), new TestHttpClientFactory(client));
        await vm.InitializeAsync(id, null, null, null, null);

        vm.Model.Name = "Updated";
        var res = await vm.SaveAsync();
        Assert.NotNull(res);
        Assert.Null(vm.Error);
    }

    [Fact]
    public async Task SaveAsync_New_Success()
    {
        var id = Guid.NewGuid();
        var plan = new SavingsPlanDto(id, "Created", SavingsPlanType.OneTime, null, null, null, true, DateTime.UtcNow, null, null);
        var client = CreateHttpClient(req =>
        {
            if (req.Method == HttpMethod.Get && req.RequestUri!.AbsolutePath == "/api/savings-plan-categories")
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(CategoriesJson(), Encoding.UTF8, "application/json") };
            }
            if (req.Method == HttpMethod.Post && req.RequestUri!.AbsolutePath == "/api/savings-plans")
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(PlanJson(plan), Encoding.UTF8, "application/json") };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var vm = new SavingsPlanEditViewModel(CreateSp(), new TestHttpClientFactory(client));
        await vm.InitializeAsync(null, null, null, null, null);

        vm.Model.Name = "Created";
        var res = await vm.SaveAsync();
        Assert.NotNull(res);
        Assert.Equal(id, res!.Id);
    }

    [Fact]
    public async Task Archive_Delete_Set_Error_On_Fail()
    {
        var id = Guid.NewGuid();
        var plan = new SavingsPlanDto(id, "Plan A", SavingsPlanType.OneTime, null, null, null, true, DateTime.UtcNow, null, null);
        var client = CreateHttpClient(req =>
        {
            if (req.Method == HttpMethod.Get && req.RequestUri!.AbsolutePath == $"/api/savings-plans/{id}")
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(PlanJson(plan), Encoding.UTF8, "application/json") };
            }
            if (req.Method == HttpMethod.Get && req.RequestUri!.AbsolutePath == "/api/savings-plan-categories")
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(CategoriesJson(), Encoding.UTF8, "application/json") };
            }
            if (req.Method == HttpMethod.Post && req.RequestUri!.AbsolutePath == $"/api/savings-plans/{id}/archive")
            {
                return new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("bad", Encoding.UTF8, "text/plain") };
            }
            if (req.Method == HttpMethod.Delete && req.RequestUri!.AbsolutePath == $"/api/savings-plans/{id}")
            {
                return new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("bad", Encoding.UTF8, "text/plain") };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var vm = new SavingsPlanEditViewModel(CreateSp(), new TestHttpClientFactory(client));
        await vm.InitializeAsync(id, null, null, null, null);

        var ok1 = await vm.ArchiveAsync();
        Assert.False(ok1);
        Assert.NotNull(vm.Error);

        var ok2 = await vm.DeleteAsync();
        Assert.False(ok2);
        Assert.NotNull(vm.Error);
    }

    [Fact]
    public void Ribbon_Disables_Save_If_Name_Short()
    {
        var vm = new SavingsPlanEditViewModel(CreateSp(), new TestHttpClientFactory(CreateHttpClient(_ => new HttpResponseMessage(HttpStatusCode.NotFound))));
        // no Id => new; Model.Name empty => Save disabled
        var loc = new TestLocalizer<SavingsPlanEditViewModelTests>();
        var groups = vm.GetRibbon(loc);
        var editGroup = groups.First(g => g.Title == "Ribbon_Group_Edit");
        var save = editGroup.Items.First(i => i.Action == "Save");
        var archive = editGroup.Items.First(i => i.Action == "Archive");
        Assert.True(save.Disabled);
        Assert.True(archive.Disabled); // new => disabled

        vm.Model.Name = "OK";
        groups = vm.GetRibbon(loc);
        save = groups.First(g => g.Title == "Ribbon_Group_Edit").Items.First(i => i.Action == "Save");
        Assert.False(save.Disabled);
    }

    [Fact]
    public async Task InitializeAsync_Loads_Analysis_For_OpenPlan_With_Postings()
    {
        var id = Guid.NewGuid();
        var catId = Guid.NewGuid();
        var plan = new SavingsPlanDto(id, "Plan Open", SavingsPlanType.Open, 0m, null, null, true, DateTime.UtcNow, null, catId, null);
        // Postings: 100,100,100,-300,50,100 -> accumulated = 150
        var analysis = new SavingsPlanAnalysisDto(id, true, 0m, null, 150m, 0m, 0);
        var cats = new[] { new SavingsPlanCategoryDto { Id = catId, Name = "Sparen" } };

        var client = CreateHttpClient(req =>
        {
            if (req.Method == HttpMethod.Get && req.RequestUri!.AbsolutePath == $"/api/savings-plans/{id}")
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(PlanJson(plan), Encoding.UTF8, "application/json") };
            }
            if (req.Method == HttpMethod.Get && req.RequestUri!.AbsolutePath == $"/api/savings-plans/{id}/analysis")
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(AnalysisJson(analysis), Encoding.UTF8, "application/json") };
            }
            if (req.Method == HttpMethod.Get && req.RequestUri!.AbsolutePath == "/api/savings-plan-categories")
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(CategoriesJson(cats), Encoding.UTF8, "application/json") };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var vm = new SavingsPlanEditViewModel(CreateSp(), new TestHttpClientFactory(client));
        await vm.InitializeAsync(id, backNav: null, draftId: null, entryId: null, prefillName: null);

        Assert.True(vm.IsEdit);
        Assert.True(vm.Loaded);
        Assert.NotNull(vm.Analysis);
        Assert.Equal(150m, vm.Analysis!.AccumulatedAmount);
        Assert.Equal(0m, vm.Analysis!.TargetAmount);
        Assert.Null(vm.Analysis!.TargetDate);
        Assert.True(vm.Analysis!.TargetReachable);
        Assert.Single(vm.Categories);
        Assert.Equal(catId, vm.Model.CategoryId);
    }
}
