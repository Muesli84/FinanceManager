using FinanceManager.Domain.Accounts;
using FinanceManager.Domain.Contacts;
using FinanceManager.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using Xunit;

namespace FinanceManager.Tests.Integration.ApiClient;

/// <summary>
/// Integration tests for the account list/search/statistics HTTP contract.
/// </summary>
public sealed class ApiClientAccountsStatisticsTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    /// <summary>
    /// Initializes a new instance of the <see cref="ApiClientAccountsStatisticsTests"/> class.
    /// </summary>
    public ApiClientAccountsStatisticsTests(TestWebApplicationFactory factory)
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

    /// <summary>
    /// The statistics endpoint requires authentication.
    /// </summary>
    [Fact]
    public async Task AccountsStatistics_Unauthenticated_Returns401()
    {
        using var http = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await http.GetAsync("/api/accounts/statistics", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// List and statistics apply the same owner and search scope, while statistics are not limited to the first page.
    /// </summary>
    [Fact]
    public async Task AccountsListAndStatistics_ApplySameOwnerAndSearchScopeBeyondFirstPage()
    {
        var api = CreateClient();
        var username = $"stats_{Guid.NewGuid():N}";
        await api.Auth_RegisterAsync(new RegisterRequest(username, "Secret123", PreferredLanguage: null, TimeZoneId: "Europe/Berlin"), TestContext.Current.CancellationToken);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var owner = await db.Users.Where(u => u.UserName == username).Select(u => u.Id).SingleAsync(TestContext.Current.CancellationToken);
            var foreign = Guid.NewGuid();
            var ownBank = new Contact(owner, "Integration Bank", ContactType.Bank, null);
            var foreignBank = new Contact(foreign, "Foreign Bank", ContactType.Bank, null);
            db.Contacts.AddRange(ownBank, foreignBank);

            for (var i = 0; i < 56; i++)
            {
                var account = new Account(owner, i % 2 == 0 ? AccountType.Giro : AccountType.Savings, $"Scope Account {i:00}", $"DE{i:00}", ownBank.Id);
                account.AdjustBalance(1m);
                db.Accounts.Add(account);
            }

            var foreignAccount = new Account(foreign, AccountType.Giro, "Scope Foreign", "DE999", foreignBank.Id);
            foreignAccount.AdjustBalance(500m);
            db.Accounts.Add(foreignAccount);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var list = await api.GetAccountsAsync(skip: 0, take: 50, bankContactId: null, q: "scope", TestContext.Current.CancellationToken);
        var statistics = await api.GetAccountStatisticsAsync("scope", TestContext.Current.CancellationToken);

        list.Should().HaveCount(50);
        statistics.AccountCount.Should().Be(56);
        statistics.TotalBalance.Should().Be(56m);
        statistics.ByAccountType.Sum(g => g.NetBalance).Should().Be(statistics.TotalBalance);
        statistics.ByAccountType.Sum(g => g.GrossMagnitude).Should().Be(statistics.TotalGrossMagnitude);
        statistics.ByBankContact.Should().ContainSingle(g => g.DisplayName == "Integration Bank");
    }

    /// <summary>
    /// List and statistics share the same name/IBAN search contract, literal wildcard handling and length validation.
    /// </summary>
    [Fact]
    public async Task AccountsListAndStatistics_SearchNameAndIbanContract()
    {
        using var http = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });
        var api = new FinanceManager.Shared.ApiClient(http);
        var username = $"stats_search_{Guid.NewGuid():N}";
        await api.Auth_RegisterAsync(new RegisterRequest(username, "Secret123", PreferredLanguage: null, TimeZoneId: "Europe/Berlin"), TestContext.Current.CancellationToken);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var owner = await db.Users.Where(u => u.UserName == username).Select(u => u.Id).SingleAsync(TestContext.Current.CancellationToken);
            var bank = new Contact(owner, "Search Bank", ContactType.Bank, null);
            db.Contacts.Add(bank);
            var unicode = new Account(owner, AccountType.Giro, "MÄDCHEN Rücklage", "DE12 3456-7890", bank.Id);
            unicode.AdjustBalance(11m);
            var literalAccount = new Account(owner, AccountType.Savings, "Wildcard %_[*?]", "DE99", bank.Id);
            literalAccount.AdjustBalance(22m);
            db.Accounts.AddRange(unicode, literalAccount);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var byName = await api.GetAccountsAsync(skip: 0, take: 50, bankContactId: null, q: "mädchen", TestContext.Current.CancellationToken);
        var byNameStats = await api.GetAccountStatisticsAsync("mädchen", TestContext.Current.CancellationToken);
        var byIban = await api.GetAccountsAsync(skip: 0, take: 50, bankContactId: null, q: "de12\u00A03456-7890", TestContext.Current.CancellationToken);
        var literal = await api.GetAccountsAsync(skip: 0, take: 50, bankContactId: null, q: "%_[*?]", TestContext.Current.CancellationToken);
        var all = await api.GetAccountStatisticsAsync("   ", TestContext.Current.CancellationToken);

        byName.Should().ContainSingle(a => a.Name == "MÄDCHEN Rücklage");
        byNameStats.AccountCount.Should().Be(1);
        byNameStats.TotalBalance.Should().Be(11m);
        byIban.Should().ContainSingle(a => a.Name == "MÄDCHEN Rücklage");
        literal.Should().ContainSingle(a => a.Name == "Wildcard %_[*?]");
        all.AccountCount.Should().Be(2);

        var validSearch = new string('x', 200);
        var tooLongSearch = new string('x', 201);
        var validList = await api.GetAccountsAsync(skip: 0, take: 50, bankContactId: null, q: validSearch, TestContext.Current.CancellationToken);
        var invalidListResponse = await http.GetAsync($"/api/accounts?q={tooLongSearch}", TestContext.Current.CancellationToken);
        var invalidStatsResponse = await http.GetAsync($"/api/accounts/statistics?q={tooLongSearch}", TestContext.Current.CancellationToken);

        validList.Should().BeEmpty();
        invalidListResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        invalidStatsResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
