using FinanceManager.Shared;
using FinanceManager.Shared.Dtos.Reports;
using FinanceManager.Shared.Dtos.Users;
using FinanceManager.Web;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace FinanceManager.Tests.Integration.ApiClient;

public class ApiClientReportsTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public ApiClientReportsTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private FinanceManager.Shared.ApiClient CreateClient()
    {
        var http = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });
        return new FinanceManager.Shared.ApiClient(http);
    }

    private async Task EnsureAuthenticatedAsync(FinanceManager.Shared.ApiClient api)
    {
        var username = $"user_{Guid.NewGuid():N}";
        await api.Auth_RegisterAsync(new RegisterRequest(username, "Secret123", PreferredLanguage: null, TimeZoneId: null));
    }

    [Fact]
    public async Task Reports_Aggregates_And_Favorites_Flow()
    {
        var api = CreateClient();
        await EnsureAuthenticatedAsync(api);

        // Aggregates query minimal
        var aggReq = new ReportAggregatesQueryRequest(PostingKind: 0, Interval: 0, Take: 6, IncludeCategory: false, ComparePrevious: false, CompareYear: false, AnalysisDate: null, PostingKinds: null, Filters: null, UseValutaDate: false);
        var agg = await api.Reports_QueryAggregatesAsync(aggReq);
        agg.Should().NotBeNull();

        // List favorites initially empty
        var favs = await api.Reports_ListFavoritesAsync();
        favs.Should().NotBeNull();

        // Create favorite
        var createReq = new ReportFavoriteCreateApiRequest
        {
            Name = $"Fav_{Guid.NewGuid():N}",
            PostingKind = 0,
            IncludeCategory = false,
            Interval = 0,
            Take = 6,
            ComparePrevious = false,
            CompareYear = false,
            ShowChart = false,
            Expandable = false,
            UseValutaDate = false
        };
        var created = await api.Reports_CreateFavoriteAsync(createReq);
        created.Should().NotBeNull();

        // Get by id
        var got = await api.Reports_GetFavoriteAsync(created.Id);
        got.Should().NotBeNull();
        got!.Id.Should().Be(created.Id);

        // Update
        var updateReq = new ReportFavoriteUpdateApiRequest
        {
            Name = created.Name + "_X",
            PostingKind = created.PostingKind,
            IncludeCategory = created.IncludeCategory,
            Interval = (int)created.Interval,
            Take = created.Take,
            ComparePrevious = created.ComparePrevious,
            CompareYear = created.CompareYear,
            ShowChart = created.ShowChart,
            Expandable = created.Expandable,
            UseValutaDate = created.UseValutaDate,
            PostingKinds = created.PostingKinds,
            Filters = created.Filters
        };
        var updated = await api.Reports_UpdateFavoriteAsync(created.Id, updateReq);
        updated.Should().NotBeNull();
        updated!.Name.Should().Be(createReq.Name + "_X");

        // Delete
        var del = await api.Reports_DeleteFavoriteAsync(created.Id);
        del.Should().BeTrue();
        var gone = await api.Reports_GetFavoriteAsync(created.Id);
        gone.Should().BeNull();
    }
}
