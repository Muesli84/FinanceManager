using FinanceManager.Domain.Postings;
using FinanceManager.Domain.Securities;
using FinanceManager.Domain.Statements;
using FinanceManager.Application.Demo;
using FinanceManager.Infrastructure;
using FinanceManager.Shared.Dtos.Budget;
using FinanceManager.Shared.Dtos.HomeKpi;
using FinanceManager.Shared.Dtos.Postings;
using FinanceManager.Shared.Dtos.Securities;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FinanceManager.Tests.Integration.ApiClient;

/// <summary>
/// Integration tests for full demo-data generation via the public API endpoint.
/// </summary>
[Collection("DemoDataSerial")]
public class ApiClientDemoDataTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    /// <summary>
    /// Initializes a new instance of the <see cref="ApiClientDemoDataTests"/> class.
    /// </summary>
    /// <param name="factory">Shared application factory for integration testing.</param>
    public ApiClientDemoDataTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private FinanceManager.Shared.ApiClient CreateClient()
    {
        var http = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        return new FinanceManager.Shared.ApiClient(http);
    }

    private async Task<(FinanceManager.Shared.ApiClient Api, Guid UserId)> CreateAuthenticatedUserAsync()
    {
        var api = CreateClient();
        var username = $"demouser_{Guid.NewGuid():N}";
        await api.Auth_RegisterAsync(new FinanceManager.Shared.Dtos.Users.RegisterRequest(username, "Secret123", null, null), CancellationToken.None);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userId = await db.Users
            .Where(x => x.UserName == username)
            .Select(x => x.Id)
            .FirstAsync(CancellationToken.None);

        return (api, userId);
    }

    private async Task CreateDemoDataForUserAsync(Guid userId)
    {
        using var scope = _factory.Services.CreateScope();
        var demoDataService = scope.ServiceProvider.GetRequiredService<IDemoDataService>();
        await demoDataService.CreateDemoDataAsync(userId, true, CancellationToken.None);
    }

    /// <summary>
    /// Verifies that the public demo-data endpoint returns a successful response for an authenticated user.
    /// </summary>
    [Fact]
    public async Task Users_CreateDemoData_ShouldReturnAccepted_WhenCalledThroughApi()
    {
        var (api, userId) = await CreateAuthenticatedUserAsync();

        await api.Users_CreateDemoDataAsync(userId, false, CancellationToken.None);
    }

    /// <summary>
    /// Verifies that the endpoint creates the required contacts, savings plans, budgets,
    /// securities, imported price histories and security buy postings.
    /// </summary>
    [Fact]
    public async Task CreateDemoDataAsync_ShouldCreateCompleteSeedSet_WhenCreatePostingsTrue()
    {
        var (_, userId) = await CreateAuthenticatedUserAsync();
        await CreateDemoDataForUserAsync(userId);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var contacts = await db.Contacts
            .AsNoTracking()
            .Where(x => x.OwnerUserId == userId)
            .Select(x => x.Name)
            .ToListAsync(CancellationToken.None);
        contacts.Should().Contain(new[]
        {
            "Mama",
            "Arbeitgeber GmbH",
            "Zentrial Versicherung",
            "SDAC",
            "Sabbel Lüchtenhausen",
            "Adli",
            "Didl",
            "Adeka",
            "Bäckerei Kramphove",
            "Bäckerei Feiping",
            "Bäckerei Schlonz"
        });

        var savingsPlans = await db.SavingsPlans
            .AsNoTracking()
            .Where(x => x.OwnerUserId == userId)
            .Select(x => new { x.Name, x.TargetAmount, x.ContractNumber })
            .ToListAsync(CancellationToken.None);
        savingsPlans.Should().Contain(x => x.Name == "SDAC Gebühr" && x.TargetAmount == 99.00m && !string.IsNullOrWhiteSpace(x.ContractNumber));
        savingsPlans.Should().Contain(x => x.Name == "Hausratversicherung" && x.TargetAmount == 62.60m && !string.IsNullOrWhiteSpace(x.ContractNumber));
        savingsPlans.Should().Contain(x => x.Name == "Auto" && x.TargetAmount == 14000.00m);
        savingsPlans.Should().Contain(x => x.Name == "Urlaub" && x.TargetAmount == null);

        var budgetPurposes = await db.BudgetPurposes
            .AsNoTracking()
            .Where(x => x.OwnerUserId == userId)
            .Select(x => new { x.Name, x.ValuationType })
            .ToListAsync(CancellationToken.None);
        budgetPurposes.Should().Contain(x => x.Name == "Gehalt");
        budgetPurposes.Should().Contain(x => x.Name == "Rückstellung Hausratversicherung");
        budgetPurposes.Should().Contain(x => x.Name == "Hausratversicherung");
        budgetPurposes.Should().Contain(x => x.Name == "Rückstellung SDAC");
        budgetPurposes.Should().Contain(x => x.Name == "SDAC");
        budgetPurposes.Should().Contain(x => x.Name == "Wohnungsmiete");
        budgetPurposes.Should().Contain(x => x.Name == "Supermärkte & Einzelhandel" && x.ValuationType == BudgetValuationType.TotalBudget);
        budgetPurposes.Should().Contain(x => x.Name == "Bäckereien & Cafés" && x.ValuationType == BudgetValuationType.TotalBudget);

        var securities = await db.Securities
            .AsNoTracking()
            .Where(x => x.OwnerUserId == userId)
            .Select(x => new { x.Id, x.Name, x.Identifier, x.CurrencyCode, x.AlphaVantageCode, x.Region, x.Sector, x.Description })
            .ToListAsync(CancellationToken.None);
        securities.Should().Contain(x => x.Name == "USHSIV-MSCI WLD"
                                         && x.Identifier == "LU00ABACAD96"
                                         && x.CurrencyCode == "EUR"
                                         && string.IsNullOrEmpty(x.AlphaVantageCode)
                                         && x.Region == "Global"
                                         && x.Sector == "MSCI World"
                                         && x.Description == "UShares MSCI World ETF");
        securities.Should().Contain(x => x.Name == "Inländische Post AG"
                                         && x.Identifier == "DE0001112026"
                                         && x.CurrencyCode == "EUR"
                                         && x.Region == "DE"
                                         && x.Sector == "Logistik");

        var worldSecurityId = securities.Single(x => x.Name == "USHSIV-MSCI WLD").Id;
        var postSecurityId = securities.Single(x => x.Name == "Inländische Post AG").Id;
        var referenceMonthStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
        var firstPriceDate = referenceMonthStart.AddYears(-2);
        var expectedBusinessDays = CountBusinessDays(firstPriceDate, referenceMonthStart);

        var worldPrices = await db.SecurityPrices
            .AsNoTracking()
            .Where(x => x.SecurityId == worldSecurityId)
            .OrderBy(x => x.Date)
            .ToListAsync(CancellationToken.None);
        var postPrices = await db.SecurityPrices
            .AsNoTracking()
            .Where(x => x.SecurityId == postSecurityId)
            .OrderBy(x => x.Date)
            .ToListAsync(CancellationToken.None);

        worldPrices.Should().HaveCount(expectedBusinessDays);
        postPrices.Should().HaveCount(expectedBusinessDays);
        worldPrices.First().Close.Should().Be(11.36m);
        postPrices.First().Close.Should().Be(44.25m);

        var homeKpis = await db.HomeKpis
            .AsNoTracking()
            .Where(x => x.OwnerUserId == userId)
            .OrderBy(x => x.SortOrder)
            .Select(x => new { x.Kind, x.PredefinedType, x.DisplayMode, x.SortOrder })
            .ToListAsync(CancellationToken.None);
        homeKpis.Should().HaveCount(5);
        homeKpis.Should().Equal(
            new { Kind = HomeKpiKind.Predefined, PredefinedType = (HomeKpiPredefined?)HomeKpiPredefined.AccountsAggregates, DisplayMode = HomeKpiDisplayMode.TotalOnly, SortOrder = 0 },
            new { Kind = HomeKpiKind.Predefined, PredefinedType = (HomeKpiPredefined?)HomeKpiPredefined.SavingsPlanAggregates, DisplayMode = HomeKpiDisplayMode.TotalOnly, SortOrder = 1 },
            new { Kind = HomeKpiKind.Predefined, PredefinedType = (HomeKpiPredefined?)HomeKpiPredefined.SecuritiesDividends, DisplayMode = HomeKpiDisplayMode.TotalOnly, SortOrder = 2 },
            new { Kind = HomeKpiKind.Predefined, PredefinedType = (HomeKpiPredefined?)HomeKpiPredefined.MonthlyBudget, DisplayMode = HomeKpiDisplayMode.TotalOnly, SortOrder = 3 },
            new { Kind = HomeKpiKind.Predefined, PredefinedType = (HomeKpiPredefined?)HomeKpiPredefined.OpenStatementDraftsCount, DisplayMode = HomeKpiDisplayMode.TotalOnly, SortOrder = 4 });

        var buys = await db.Postings
            .AsNoTracking()
            .Where(x => x.Kind == PostingKind.Security && x.SecuritySubType == SecurityPostingSubType.Buy)
            .Where(x => x.SecurityId == worldSecurityId || x.SecurityId == postSecurityId)
            .ToListAsync(CancellationToken.None);
        buys.Should().ContainSingle(x => x.SecurityId == worldSecurityId);
        buys.Should().ContainSingle(x => x.SecurityId == postSecurityId);

        var firstMonth = referenceMonthStart.AddMonths(-23);
        var expectedWorldBuyMonthStart = firstMonth.AddMonths(2);
        var worldBuy = buys.Single(x => x.SecurityId == worldSecurityId);
        worldBuy.BookingDate.Year.Should().Be(expectedWorldBuyMonthStart.Year);
        worldBuy.BookingDate.Month.Should().Be(expectedWorldBuyMonthStart.Month);

        var worldDividends = await db.Postings
            .AsNoTracking()
            .Where(x => x.Kind == PostingKind.Security && x.SecuritySubType == SecurityPostingSubType.Dividend)
            .Where(x => x.SecurityId == worldSecurityId)
            .OrderBy(x => x.BookingDate)
            .ToListAsync(CancellationToken.None);
        worldDividends.Should().NotBeEmpty();
        worldDividends.All(x => x.BookingDate >= expectedWorldBuyMonthStart).Should().BeTrue();

        var userAccountIds = await db.Accounts
            .AsNoTracking()
            .Where(x => x.OwnerUserId == userId)
            .Select(x => x.Id)
            .ToListAsync(CancellationToken.None);
        var startgeld = await db.Postings
            .AsNoTracking()
            .Where(x => x.Subject == "Startgeld" && x.Amount == 5000.00m)
            .Where(x => x.AccountId.HasValue && userAccountIds.Contains(x.AccountId.Value))
            .ToListAsync(CancellationToken.None);
        startgeld.Should().ContainSingle();
        startgeld[0].BookingDate.Year.Should().Be(firstMonth.Year);
        startgeld[0].BookingDate.Month.Should().Be(firstMonth.Month);
    }

    /// <summary>
    /// Verifies that drafts for the current month remain unbooked and therefore do not create
    /// postings in the current month.
    /// </summary>
    [Fact]
    public async Task CreateDemoDataAsync_ShouldSkipCurrentMonthPostings()
    {
        var (_, userId) = await CreateAuthenticatedUserAsync();
        await CreateDemoDataForUserAsync(userId);

        var now = DateTime.UtcNow;
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var postingsCurrentMonth = await db.Postings
            .AsNoTracking()
            .Where(x => x.AccountId != null)
            .Where(x => x.BookingDate.Year == now.Year && x.BookingDate.Month == now.Month)
            .CountAsync(CancellationToken.None);
        postingsCurrentMonth.Should().Be(0);

        var currentMonthDrafts = await db.StatementDrafts
            .AsNoTracking()
            .Where(x => x.OwnerUserId == userId && x.Status == StatementDraftStatus.Draft)
            .ToListAsync(CancellationToken.None);
        currentMonthDrafts.Should().HaveCount(3);
    }

    /// <summary>
    /// Verifies reproducibility by comparing a deterministic sample of generated card postings
    /// across two independently seeded users.
    /// </summary>
    [Fact]
    public async Task CreateDemoDataAsync_ShouldUseDeterministicSeed()
    {
        var (_, userIdA) = await CreateAuthenticatedUserAsync();
        await CreateDemoDataForUserAsync(userIdA);
        var (_, userIdB) = await CreateAuthenticatedUserAsync();
        await CreateDemoDataForUserAsync(userIdB);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var accountIdsA = await db.Accounts
            .AsNoTracking()
            .Where(x => x.OwnerUserId == userIdA)
            .Select(x => x.Id)
            .ToListAsync(CancellationToken.None);
        var accountIdsB = await db.Accounts
            .AsNoTracking()
            .Where(x => x.OwnerUserId == userIdB)
            .Select(x => x.Id)
            .ToListAsync(CancellationToken.None);

        var sampleA = await db.Postings
            .AsNoTracking()
            .Where(x => x.AccountId != null)
            .Where(x => x.Subject != null && x.Subject.StartsWith("Kartenzahlung"))
            .Where(x => accountIdsA.Contains(x.AccountId!.Value))
            .OrderBy(x => x.BookingDate)
            .ThenBy(x => x.Subject)
            .Select(x => new { x.BookingDate, x.Subject, x.Amount })
            .Take(40)
            .ToListAsync(CancellationToken.None);

        var sampleB = await db.Postings
            .AsNoTracking()
            .Where(x => x.AccountId != null)
            .Where(x => x.Subject != null && x.Subject.StartsWith("Kartenzahlung"))
            .Where(x => accountIdsB.Contains(x.AccountId!.Value))
            .OrderBy(x => x.BookingDate)
            .ThenBy(x => x.Subject)
            .Select(x => new { x.BookingDate, x.Subject, x.Amount })
            .Take(40)
            .ToListAsync(CancellationToken.None);

        sampleA.Should().HaveCount(40);
        sampleB.Should().HaveCount(40);
        sampleA.Should().BeEquivalentTo(sampleB);
    }

    private static int CountBusinessDays(DateTime start, DateTime end)
    {
        var count = 0;
        for (var day = start.Date; day <= end.Date; day = day.AddDays(1))
        {
            if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                continue;
            }

            count++;
        }

        return count;
    }
}
