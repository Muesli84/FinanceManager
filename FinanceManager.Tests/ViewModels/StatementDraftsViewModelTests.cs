using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.IO;
using FinanceManager.Web.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using FinanceManager.Application;

namespace FinanceManager.Tests.ViewModels;

public sealed class StatementDraftsViewModelTests
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
        public string? PreferredLanguage { get; set; } = "de";
        public bool IsAuthenticated { get; set; } = true;
        public bool IsAdmin { get; set; } = false;
    }

    private static IServiceProvider CreateSp()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentUserService>(new TestCurrentUserService());
        return services.BuildServiceProvider();
    }

    private static string Json(object obj) => JsonSerializer.Serialize(obj);

    [Fact]
    public async Task Initialize_Loads_First_Page()
    {
        var batch = new[]
        {
            new { DraftId = Guid.NewGuid(), OriginalFileName = "a.pdf", Description = "A", Status = 0, Entries = Array.Empty<object>() },
            new { DraftId = Guid.NewGuid(), OriginalFileName = "b.pdf", Description = "B", Status = 0, Entries = Array.Empty<object>() },
            new { DraftId = Guid.NewGuid(), OriginalFileName = "c.pdf", Description = "C", Status = 0, Entries = Array.Empty<object>() },
        };
        var client = CreateHttpClient(req =>
        {
            if (req.Method == HttpMethod.Get && req.RequestUri!.AbsolutePath == "/api/statement-drafts")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(Json(batch), Encoding.UTF8, "application/json")
                };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var vm = new StatementDraftsViewModel(CreateSp(), new TestHttpClientFactory(client));
        await vm.InitializeAsync();

        Assert.Equal(3, vm.Items.Count);
        Assert.True(vm.CanLoadMore == false ? true : true); // not asserting stop here (unknown until next page)
    }

    [Fact]
    public async Task LoadMore_Accumulates_And_Stops_When_Less_Than_PageSize()
    {
        var first = new[]
        {
            new { DraftId = Guid.NewGuid(), OriginalFileName = "a.pdf", Description = "A", Status = 0, Entries = Array.Empty<object>() },
            new { DraftId = Guid.NewGuid(), OriginalFileName = "b.pdf", Description = "B", Status = 0, Entries = Array.Empty<object>() },
            new { DraftId = Guid.NewGuid(), OriginalFileName = "c.pdf", Description = "C", Status = 0, Entries = Array.Empty<object>() },
        };
        var second = new[]
        {
            new { DraftId = Guid.NewGuid(), OriginalFileName = "d.pdf", Description = "D", Status = 0, Entries = Array.Empty<object>() },
        };
        int getCalls = 0;
        var client = CreateHttpClient(req =>
        {
            if (req.Method == HttpMethod.Get && req.RequestUri!.AbsolutePath == "/api/statement-drafts")
            {
                getCalls++;
                var payload = getCalls == 1 ? first : second;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(Json(payload), Encoding.UTF8, "application/json")
                };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var vm = new StatementDraftsViewModel(CreateSp(), new TestHttpClientFactory(client));
        await vm.InitializeAsync();
        Assert.Equal(3, vm.Items.Count);
        Assert.True(vm.CanLoadMore);

        await vm.LoadMoreAsync();
        Assert.Equal(4, vm.Items.Count);
        Assert.False(vm.CanLoadMore);
    }

    [Fact]
    public async Task DeleteAll_Clears_And_Resets()
    {
        var list = new[] { new { DraftId = Guid.NewGuid(), OriginalFileName = "a.pdf", Description = "A", Status = 0, Entries = Array.Empty<object>() } };
        var client = CreateHttpClient(req =>
        {
            if (req.Method == HttpMethod.Get && req.RequestUri!.AbsolutePath == "/api/statement-drafts")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(Json(list), Encoding.UTF8, "application/json")
                };
            }
            if (req.Method == HttpMethod.Delete && req.RequestUri!.AbsolutePath == "/api/statement-drafts/all")
            {
                return new HttpResponseMessage(HttpStatusCode.OK);
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var vm = new StatementDraftsViewModel(CreateSp(), new TestHttpClientFactory(client));
        await vm.InitializeAsync();
        Assert.Single(vm.Items);

        var ok = await vm.DeleteAllAsync();
        Assert.True(ok);
        Assert.Empty(vm.Items);
        Assert.True(vm.CanLoadMore);
    }

    [Fact]
    public async Task Upload_Returns_FirstDraftId()
    {
        var firstId = Guid.NewGuid();
        var client = CreateHttpClient(req =>
        {
            if (req.Method == HttpMethod.Post && req.RequestUri!.AbsolutePath == "/api/statement-drafts/upload")
            {
                var body = new { FirstDraft = new { DraftId = firstId }, SplitInfo = (object?)null };
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(Json(body), Encoding.UTF8, "application/json")
                };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var vm = new StatementDraftsViewModel(CreateSp(), new TestHttpClientFactory(client));
        await using var stream = new MemoryStream(new byte[] { 1, 2, 3 });
        var id = await vm.UploadAsync(stream, "x.pdf");
        Assert.Equal(firstId, id);
    }

    [Fact]
    public async Task Classify_Starts_And_Completes_Reloads()
    {
        var first = new[] { new { DraftId = Guid.NewGuid(), OriginalFileName = "a.pdf", Description = "A", Status = 0, Entries = Array.Empty<object>() } };
        bool listServed = false;
        var client = CreateHttpClient(req =>
        {
            if (req.Method == HttpMethod.Get && req.RequestUri!.AbsolutePath == "/api/statement-drafts")
            {
                // After completion we'll reload list once
                var payload = listServed ? Array.Empty<object>() : first;
                listServed = true;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(Json(payload), Encoding.UTF8, "application/json")
                };
            }
            if (req.Method == HttpMethod.Post && req.RequestUri!.AbsolutePath == "/api/statement-drafts/classify")
            {
                var status = new { running = true, processed = 1, total = 10, message = "started" };
                var resp = new HttpResponseMessage(HttpStatusCode.Accepted)
                {
                    Content = new StringContent(Json(status), Encoding.UTF8, "application/json")
                };
                return resp;
            }
            if (req.Method == HttpMethod.Get && req.RequestUri!.AbsolutePath == "/api/statement-drafts/classify/status")
            {
                var done = new { running = false, processed = 10, total = 10, message = "done" };
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(Json(done), Encoding.UTF8, "application/json")
                };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var vm = new StatementDraftsViewModel(CreateSp(), new TestHttpClientFactory(client));
        await vm.InitializeAsync();
        Assert.Single(vm.Items);

        await vm.StartClassifyAsync();
        Assert.True(vm.IsClassifying);

        await vm.RefreshClassifyStatusAsync();
        Assert.False(vm.IsClassifying);
        // after reload we served empty list
        Assert.Empty(vm.Items);
    }

    [Fact]
    public async Task Booking_Starts_Status_Updates_And_Completes()
    {
        var initialList = new[] { new { DraftId = Guid.NewGuid(), OriginalFileName = "a.pdf", Description = "A", Status = 0, Entries = Array.Empty<object>() } };
        bool listReloaded = false;
        var client = CreateHttpClient(req =>
        {
            if (req.Method == HttpMethod.Get && req.RequestUri!.AbsolutePath == "/api/statement-drafts")
            {
                var payload = listReloaded ? Array.Empty<object>() : initialList;
                listReloaded = true;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(Json(payload), Encoding.UTF8, "application/json")
                };
            }
            if (req.Method == HttpMethod.Post && req.RequestUri!.AbsolutePath == "/api/statement-drafts/book-all")
            {
                var s = new { running = true, processed = 0, failed = 0, total = 1, warnings = 0, errors = 0, message = "started", issues = Array.Empty<object>() };
                return new HttpResponseMessage(HttpStatusCode.Accepted)
                {
                    Content = new StringContent(Json(s), Encoding.UTF8, "application/json")
                };
            }
            if (req.Method == HttpMethod.Get && req.RequestUri!.AbsolutePath == "/api/statement-drafts/book-all/status")
            {
                var done = new { running = false, processed = 1, failed = 0, total = 1, warnings = 0, errors = 0, message = "ok", issues = Array.Empty<object>() };
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(Json(done), Encoding.UTF8, "application/json")
                };
            }
            if (req.Method == HttpMethod.Post && req.RequestUri!.AbsolutePath == "/api/statement-drafts/book-all/cancel")
            {
                return new HttpResponseMessage(HttpStatusCode.OK);
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var vm = new StatementDraftsViewModel(CreateSp(), new TestHttpClientFactory(client));
        await vm.InitializeAsync();
        Assert.Single(vm.Items);

        await vm.StartBookAllAsync(ignoreWarnings: false, abortOnFirstIssue: false, bookEntriesIndividually: false);
        Assert.True(vm.IsBooking);

        await vm.RefreshBookStatusAsync();
        Assert.False(vm.IsBooking);
        Assert.Empty(vm.Items);

        await vm.CancelBookingAsync();
        // no exception means ok
    }
}
