using FinanceManager.Application;
using FinanceManager.Web.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Text;
using System.Text.Json;

namespace FinanceManager.Tests.ViewModels;

public sealed class PostingDetailViewModelTests
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
        return services.BuildServiceProvider();
    }

    private static string PostingJson(Guid id, Guid groupId, Guid? accountId = null, Guid? contactId = null, Guid? planId = null, Guid? securityId = null)
    {
        var obj = new
        {
            Id = id,
            BookingDate = DateTime.UtcNow.Date.ToString("O"),
            Amount = 123.45m,
            Kind = 0,
            AccountId = accountId,
            ContactId = contactId,
            SavingsPlanId = planId,
            SecurityId = securityId,
            SourceId = Guid.NewGuid(),
            Subject = "Subj",
            RecipientName = "Rec",
            Description = "Desc",
            SecuritySubType = (int?)null,
            Quantity = (decimal?)null,
            GroupId = groupId
        };
        return JsonSerializer.Serialize(obj);
    }

    private static string LinksJson(Guid? accountId, Guid? contactId, Guid? planId, Guid? securityId)
    {
        var obj = new { AccountId = accountId, ContactId = contactId, SavingsPlanId = planId, SecurityId = securityId };
        return JsonSerializer.Serialize(obj);
    }

    [Fact]
    public async Task Initialize_LoadsDetail_ResolvesLinks()
    {
        var id = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var fromPostingAccountId = (Guid?)null; // missing here
        var linkedAccountId = Guid.NewGuid();

        var client = CreateHttpClient(req =>
        {
            if (req.Method == HttpMethod.Get && req.RequestUri!.AbsolutePath == $"/api/postings/{id}")
            {
                var json = PostingJson(id, groupId, fromPostingAccountId);
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
            }
            if (req.Method == HttpMethod.Get && req.RequestUri!.AbsolutePath == $"/api/postings/group/{groupId}")
            {
                var json = LinksJson(linkedAccountId, null, null, null);
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var vm = new PostingDetailViewModel(CreateSp(), new TestHttpClientFactory(client));
        vm.Configure(id);

        await vm.InitializeAsync();

        Assert.False(vm.Loading);
        Assert.NotNull(vm.Detail);
        Assert.Equal(linkedAccountId, vm.LinkedAccountId);
        Assert.False(vm.LinksLoading);
    }
}
