using System;
using System.IO;
using FinanceManager.Shared;
using FinanceManager.Shared.Dtos.Securities;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace FinanceManager.Tests.Integration.ApiClient;

public class ApiClientSecuritiesTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public ApiClientSecuritiesTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private Shared.ApiClient CreateClient()
    {
        var http = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });
        return new Shared.ApiClient(http);
    }

    private async Task EnsureAuthenticatedAsync(Shared.ApiClient api)
    {
        var username = $"user_{Guid.NewGuid():N}";
        await api.Auth_RegisterAsync(new Shared.Dtos.Users.RegisterRequest(username, "Secret123", PreferredLanguage: null, TimeZoneId: null));
    }

    [Fact]
    public async Task Securities_Flow_CRUD_Symbol_Prices_Aggregates_Dividends_Backfill()
    {
        var api = CreateClient();
        await EnsureAuthenticatedAsync(api);

        // initial list and count
        var list = await api.Securities_ListAsync();
        list.Should().NotBeNull();
        var cnt = await api.Securities_CountAsync();
        cnt.Should().BeGreaterThanOrEqualTo(0);

        // create
        var createReq = new SecurityRequest
        {
            Name = "Tesla",
            Identifier = "TSLA",
            Description = null,
            AlphaVantageCode = null,
            CurrencyCode = "USD",
            CategoryId = null
        };
        var created = await api.Securities_CreateAsync(createReq);
        created.Should().NotBeNull();
        created!.Name.Should().Be("Tesla");

        // get
        var got = await api.Securities_GetAsync(created.Id);
        got.Should().NotBeNull();
        got!.Id.Should().Be(created.Id);

        // update
        var updateReq = new SecurityRequest
        {
            Name = "Tesla Inc.",
            Identifier = "TSLA",
            Description = "Electric vehicles",
            AlphaVantageCode = null,
            CurrencyCode = "USD",
            CategoryId = null
        };
        var updated = await api.Securities_UpdateAsync(created.Id, updateReq);
        updated.Should().NotBeNull();
        updated!.Name.Should().Be("Tesla Inc.");

        // set/clear symbol via fake attachment upload (not asserting content, just route)
        using var ms = new MemoryStream(new byte[] { 1, 2, 3 });
        var attachment = await api.Securities_UploadSymbolAsync(created.Id, ms, "logo.png", "image/png", null);
        attachment.Should().NotBeNull();
        var setOk = await api.Securities_SetSymbolAsync(created.Id, attachment.Id);
        setOk.Should().BeTrue();
        var clearOk = await api.Securities_ClearSymbolAsync(created.Id);
        clearOk.Should().BeTrue();

        // aggregates
        var aggs = await api.Securities_GetAggregatesAsync(created.Id, period: "Month", take: 12);
        aggs.Should().NotBeNull();

        // prices
        var prices = await api.Securities_GetPricesAsync(created.Id, skip: 0, take: 10);
        prices.Should().NotBeNull();

        // dividends
        var dividends = await api.Securities_GetDividendsAsync(period: null, take: null);
        dividends.Should().NotBeNull();

        // backfill enqueue
        var info = await api.Securities_EnqueueBackfillAsync(created.Id, DateTime.UtcNow.AddYears(-1), DateTime.UtcNow);
        info.Should().NotBeNull();

        // archive then delete
        var archived = await api.Securities_ArchiveAsync(created.Id);
        archived.Should().BeTrue();
        var deleted = await api.Securities_DeleteAsync(created.Id);
        deleted.Should().BeTrue();
        var gone = await api.Securities_GetAsync(created.Id);
        gone.Should().BeNull();
    }
}
