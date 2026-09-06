using System.Globalization;
using FinanceManager.Application.Securities.ReturnAnalysis;
using FinanceManager.Domain.Postings;
using FinanceManager.Domain.Securities;
using FinanceManager.Domain.Users;
using FinanceManager.Infrastructure;
using FinanceManager.Infrastructure.Securities.ReturnAnalysis;
using FinanceManager.Tests.TestHelpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace FinanceManager.Tests.Securities;

/// <summary>
/// Integration tests for <see cref="ReturnAnalysisService"/> covering user-scoping,
/// security ownership, and orchestration of return calculations.
/// Uses EF Core InMemory database with a fresh instance per test.
/// </summary>
public sealed class ReturnAnalysisServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly Mock<IReturnCalculationService> _calcMock = new();
    private readonly Mock<IFifoCostBasisCalculator> _fifoMock = new();
    private readonly IMemoryCache _memoryCache;
    private readonly MemoryReturnAnalysisCache _cache;
    private readonly ReturnAnalysisService _sut;

    /// <summary>Initializes a fresh DB and SUT for each test.</summary>
    public ReturnAnalysisServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _memoryCache = new MemoryCache(new MemoryCacheOptions());
        _cache = new MemoryReturnAnalysisCache(_memoryCache);

        // Default FIFO mock: 10 shares @ 100 = 1000, no oversell
        _fifoMock
            .Setup(f => f.Calculate(It.IsAny<IReadOnlyList<SecurityTransaction>>()))
            .Returns(new FifoCostBasisResult(1_000m, 0m, Array.Empty<FifoLot>(), 10m, false, null, 0m));

        // Default calc mocks: return zero / null where applicable
        _calcMock.Setup(c => c.CalculateTotalReturn(It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<decimal>()))
            .Returns(0.05m);
        _calcMock.Setup(c => c.CalculateCagr(It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<double>()))
            .Returns((decimal?)null);
        _calcMock.Setup(c => c.CalculateIrr(It.IsAny<IReadOnlyList<CashflowPoint>>()))
            .Returns((decimal?)null);
        _calcMock.Setup(c => c.CalculateTwr(It.IsAny<IReadOnlyList<TwrPeriodInput>>()))
            .Returns((decimal?)null);
        _calcMock.Setup(c => c.CalculateVolatility(It.IsAny<IReadOnlyList<decimal>>()))
            .Returns((decimal?)null);
        _calcMock.Setup(c => c.CalculateMaxDrawdown(It.IsAny<IReadOnlyList<decimal>>()))
            .Returns((decimal?)null);
        _calcMock.Setup(c => c.CalculateDividendYield(It.IsAny<decimal>(), It.IsAny<decimal>()))
            .Returns((decimal?)null);
        _calcMock.Setup(c => c.CalculateTaxRate(It.IsAny<decimal>(), It.IsAny<decimal>()))
            .Returns((decimal?)null);
        _calcMock.Setup(c => c.CalculateSharpeRatio(It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<decimal>()))
            .Returns((decimal?)null);

        _sut = new ReturnAnalysisService(
            _db,
            _calcMock.Object,
            _fifoMock.Object,
            _cache,
            NullLogger<ReturnAnalysisService>.Instance,
            new TestReturnAnalysisLocalizer());
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _memoryCache.Dispose();
        _db.Dispose();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a security and its owning user, persists both and returns them.
    /// </summary>
    /// <param name="security">The security.</param>
    private (Security security, User user) SetupSecurityAndUser()
    {
        var user = new User($"user-{Guid.NewGuid():N}", "hash");
        _db.Users.Add(user);

        var security = new Security(user.Id, "Test Security", "TST", null, null, "EUR", null);
        _db.Securities.Add(security);
        _db.SaveChanges();
        return (security, user);
    }

    /// <summary>
    /// Creates a Buy-type <see cref="Posting"/> for the given security without persisting it.
    /// </summary>
    /// <param name="securityId">The security id.</param>
    /// <param name="date">The date.</param>
    /// <param name="amount">The amount.</param>
    /// <param name="quantity">The quantity.</param>
    /// <returns>The result.</returns>
    private static Posting CreateBuyPosting(Guid securityId, DateTime date, decimal amount, decimal quantity)
        => new Posting(
            Guid.NewGuid(),
            PostingKind.Security,
            null, null, null,
            securityId,
            date,
            amount,
            null, null, null,
            SecurityPostingSubType.Buy,
            quantity);

    // ─────────────────────────────────────────────────────────────────────────
    // GetReturnSummaryAsync
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// When the security does not belong to the requesting user,
    /// the method must return null (user-scoping S-1 requirement).
    /// </summary>
    [Fact]
    public async Task GetReturnSummaryAsync_Should_ReturnNull_When_SecurityDoesNotBelongToUser()
    {
        // Arrange – security exists but belongs to a different user
        var (security, _) = SetupSecurityAndUser();
        var otherUserId = Guid.NewGuid(); // different user, no matching security

        // Act
        ReturnSummaryDto? result = await _sut.GetReturnSummaryAsync(security.Id, otherUserId, CancellationToken.None);

        // Assert
        result.Should().BeNull();
    }

    /// <summary>
    /// When the security is found and owned by the user but has no transactions,
    /// the method must return null.
    /// </summary>
    [Fact]
    public async Task GetReturnSummaryAsync_Should_ReturnNull_When_NoTransactionsExist()
    {
        // Arrange – security exists and is owned by user, but no postings
        var (security, user) = SetupSecurityAndUser();

        // Act
        ReturnSummaryDto? result = await _sut.GetReturnSummaryAsync(security.Id, user.Id, CancellationToken.None);

        // Assert
        result.Should().BeNull();
    }

    /// <summary>
    /// When there are transactions but no current price data available,
    /// HasMissingPrices must be true and MissingPricesHint must be set.
    /// </summary>
    [Fact]
    public async Task GetReturnSummaryAsync_Should_SetHasMissingPrices_When_NoCurrentPriceAvailable()
    {
        // Arrange – one buy posting, no price data in the database
        var (security, user) = SetupSecurityAndUser();
        var posting = CreateBuyPosting(security.Id, DateTime.Today.AddDays(-30), -1_000m, 10m);
        _db.Postings.Add(posting);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        ReturnSummaryDto? result = await _sut.GetReturnSummaryAsync(security.Id, user.Id, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.HasMissingPrices.Should().BeTrue();
        result.MissingPricesHint.Should().NotBeNullOrEmpty();
    }

    /// <summary>
    /// When the FIFO result signals an oversell warning, HasMissingPrices must be true
    /// and MissingPricesHint must be populated with the oversell warning message.
    /// </summary>
    [Fact]
    public async Task GetReturnSummaryAsync_Should_SetOversellHint_When_OversellWarningActive()
    {
        // Arrange – configure FIFO mock to return an oversell warning
        _fifoMock
            .Setup(f => f.Calculate(It.IsAny<IReadOnlyList<SecurityTransaction>>()))
            .Returns(new FifoCostBasisResult(500m, 0m, Array.Empty<FifoLot>(), 0m, true, "Oversell detected", 0m));

        var (security, user) = SetupSecurityAndUser();
        var posting = CreateBuyPosting(security.Id, DateTime.Today.AddDays(-30), -1_000m, 10m);
        _db.Postings.Add(posting);

        // Add a price so we bypass the "no current price" branch and reach the oversell check
        _db.SecurityPrices.Add(new SecurityPrice(security.Id, DateTime.Today, 105m));
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        ReturnSummaryDto? result = await _sut.GetReturnSummaryAsync(security.Id, user.Id, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.HasMissingPrices.Should().BeTrue();
        result.MissingPricesHint.Should().Be("Oversell detected");
    }

    /// <summary>
    /// Full buy–sell cycle using the real <see cref="FifoCostBasisCalculator"/>.
    /// One buy with linked fee (GroupId A) is fully liquidated by a sell with linked fee (GroupId B).
    ///
    /// Transactions (exact values from production database, Fraport AG):
    ///   Buy  25 shares  −957,75 EUR  (GroupId A)
    ///   Fee             −7,29  EUR   (GroupId A – linked to buy, added to cost basis by FIFO)
    ///   Fee             −7,78  EUR   (GroupId B – linked to sell, deducted from sales proceeds)
    ///   Sell 25 shares  +1151,50 EUR (GroupId B, Quantity stored as −25 in domain model)
    ///
    /// Expected:
    ///   TotalSharesHeld        = 0        (all sold)
    ///   CurrentMarketValue     = 0        (no remaining shares × price)
    ///   InvestedCapital        = 0        (FIFO cost basis of CURRENTLY HELD shares; fully sold → 0)
    ///   historicalInvestedCap  = 965,04   (internal only; buy 957,75 + buy-fee 7,29 → used for return calc)
    ///   salesProceeds net      = 1143,72  (sell 1151,50 − sell-fee 7,78)
    ///   TotalReturnAbsolute    = 1143,72 − 965,04 = 178,68
    /// </summary>
    [Fact]
    public async Task GetReturnSummaryAsync_Should_IncludeSalesProceedsNetOfFees_When_FullSellCycleExists()
    {
        // Arrange – use the REAL FifoCostBasisCalculator, not the class-level mock
        var realFifo = new FifoCostBasisCalculator(NullLogger<FifoCostBasisCalculator>.Instance);
        var sut = new ReturnAnalysisService(
            _db, _calcMock.Object, realFifo, _cache,
            NullLogger<ReturnAnalysisService>.Instance,
            new TestReturnAnalysisLocalizer());

        var groupBuy = new Guid("9be20b76-c600-4ab5-b388-e02a2b89bffa");
        var groupSell = new Guid("ca09994a-decd-40a7-9eff-a51d1ba581ed");
        const decimal buyAmount = -957.75m;
        const decimal buyFeeAmount = -7.29m;
        const decimal sellAmount = 1151.50m;
        const decimal sellFeeAmount = -7.78m;

        // Expected derived values
        const decimal historicalInvestedCapital = 957.75m + 7.29m;     // 965,04 — used internally for return calc
        const decimal expectedSalesProceeds = 1151.50m - 7.78m;    // 1143,72
        const decimal expectedReturnAbsolute = expectedSalesProceeds - historicalInvestedCapital; // 178,68

        var (security, user) = SetupSecurityAndUser();
        var buyDate = new DateTime(2020, 3, 26);
        var sellDate = new DateTime(2024, 8, 6);

        // Quantity on Sell is negative in the domain model (outflow of shares)
        var buyPosting = new Posting(
            Guid.NewGuid(), PostingKind.Security,
            null, null, null, security.Id,
            buyDate, buyAmount, null, null, null,
            SecurityPostingSubType.Buy, 25m)
            .SetGroup(groupBuy);

        var buyFeePosting = new Posting(
            Guid.NewGuid(), PostingKind.Security,
            null, null, null, security.Id,
            buyDate, buyFeeAmount, null, null, null,
            SecurityPostingSubType.Fee, null)
            .SetGroup(groupBuy);

        var sellFeePosting = new Posting(
            Guid.NewGuid(), PostingKind.Security,
            null, null, null, security.Id,
            sellDate, sellFeeAmount, null, null, null,
            SecurityPostingSubType.Fee, null)
            .SetGroup(groupSell);

        var sellPosting = new Posting(
            Guid.NewGuid(), PostingKind.Security,
            null, null, null, security.Id,
            sellDate, sellAmount, null, null, null,
            SecurityPostingSubType.Sell, -25m)   // negative by domain convention
            .SetGroup(groupSell);

        _db.Postings.AddRange(buyPosting, buyFeePosting, sellFeePosting, sellPosting);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        ReturnSummaryDto? result = await sut.GetReturnSummaryAsync(security.Id, user.Id, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.TotalSharesHeld.Should().Be(0m, because: "all 25 shares were sold");
        result.CurrentMarketValue.Should().Be(0m, because: "no shares remain → no market value");
        result.InvestedCapital.Should().Be(0m,
            because: "InvestedCapital shows FIFO cost basis of currently held shares; fully sold means 0");
        result.TotalReturnAbsolute.Should().BeApproximately(expectedReturnAbsolute, 0.01m,
            because: $"net sales proceeds ({expectedSalesProceeds}) minus historical invested capital ({historicalInvestedCapital}) = {expectedReturnAbsolute}");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GetSparklineDataAsync
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// When there are no transactions for the security, the method must return null.
    /// </summary>
    [Fact]
    public async Task GetSparklineDataAsync_Should_ReturnNull_When_NoTransactionsExist()
    {
        // Arrange
        var (security, user) = SetupSecurityAndUser();

        // Act
        SparklineDataDto? result = await _sut.GetSparklineDataAsync(security.Id, user.Id, CancellationToken.None);

        // Assert
        result.Should().BeNull();
    }

    /// <summary>
    /// When there are fewer than 30 price points available after the first transaction date,
    /// the method must return null (not enough data for a meaningful sparkline).
    /// </summary>
    [Fact]
    public async Task GetSparklineDataAsync_Should_ReturnNull_When_LessThan30PricePoints()
    {
        // Arrange – one transaction, only 5 price entries
        var (security, user) = SetupSecurityAndUser();
        var posting = CreateBuyPosting(security.Id, DateTime.Today.AddDays(-10), -1_000m, 10m);
        _db.Postings.Add(posting);

        for (int i = 5; i >= 1; i--)
        {
            _db.SecurityPrices.Add(new SecurityPrice(security.Id, DateTime.Today.AddDays(-i), 100m + i));
        }
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        SparklineDataDto? result = await _sut.GetSparklineDataAsync(security.Id, user.Id, CancellationToken.None);

        // Assert
        result.Should().BeNull();
    }

    /// <summary>
    /// When there are at least 30 price points, the method must return a non-null
    /// <see cref="SparklineDataDto"/> with data points.
    /// </summary>
    [Fact]
    public async Task GetSparklineDataAsync_Should_ReturnSparkline_When_Exactly30PricePoints()
    {
        // Arrange – transaction 35 days ago, 35 consecutive price entries
        var (security, user) = SetupSecurityAndUser();
        var firstDate = DateTime.Today.AddDays(-35);
        var posting = CreateBuyPosting(security.Id, firstDate, -1_000m, 10m);
        _db.Postings.Add(posting);

        for (int i = 35; i >= 1; i--)
        {
            _db.SecurityPrices.Add(new SecurityPrice(security.Id, DateTime.Today.AddDays(-i), 100m));
        }
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        SparklineDataDto? result = await _sut.GetSparklineDataAsync(security.Id, user.Id, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.Points.Should().NotBeEmpty();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GetCashflowTimelineAsync
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// When the security does not exist or does not belong to the requesting user,
    /// the method must return null.
    /// </summary>
    [Fact]
    public async Task GetCashflowTimelineAsync_Should_ReturnNull_When_SecurityNotFound()
    {
        // Arrange – non-existent security and random user
        var randomSecurityId = Guid.NewGuid();
        var randomUserId = Guid.NewGuid();

        // Act
        CashflowTimelineDto? result = await _sut.GetCashflowTimelineAsync(randomSecurityId, randomUserId, CancellationToken.None);

        // Assert
        result.Should().BeNull();
    }

    /// <summary>
    /// When the security exists and is owned by the user but there are no transactions,
    /// the method must return an empty (non-null) DTO with empty collections.
    /// This differs from other methods that return null for empty data.
    /// </summary>
    [Fact]
    public async Task GetCashflowTimelineAsync_Should_ReturnEmptyDto_When_NoTransactions()
    {
        // Arrange – security exists, but no postings
        var (security, user) = SetupSecurityAndUser();

        // Act
        CashflowTimelineDto? result = await _sut.GetCashflowTimelineAsync(security.Id, user.Id, CancellationToken.None);

        // Assert – must be non-null with empty collections (not null)
        result.Should().NotBeNull();
        result!.Entries.Should().BeEmpty();
        result.AnnualSummaries.Should().BeEmpty();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GetBenchmarkComparisonAsync
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// When no benchmark security is configured for the user (BenchmarkSecurityId is null),
    /// the method must return null.
    /// </summary>
    [Fact]
    public async Task GetBenchmarkComparisonAsync_Should_ReturnNull_When_NoBenchmarkConfigured()
    {
        // Arrange – user without BenchmarkSecurityId (default after construction)
        var (security, user) = SetupSecurityAndUser();

        // Act
        BenchmarkComparisonDto? result = await _sut.GetBenchmarkComparisonAsync(security.Id, user.Id, CancellationToken.None);

        // Assert
        result.Should().BeNull();
    }

    /// <summary>
    /// When the configured benchmark security belongs to a different user,
    /// the method must return null (ownership enforcement S-3).
    /// </summary>
    [Fact]
    public async Task GetBenchmarkComparisonAsync_Should_ReturnNull_When_BenchmarkSecurityNotOwnedByUser()
    {
        // Arrange – benchmark security belongs to a different owner
        var (security, user) = SetupSecurityAndUser();
        var otherOwner = Guid.NewGuid();
        var benchmarkSecurity = new Security(otherOwner, "Benchmark Index", "BMK", null, null, "EUR", null);
        _db.Securities.Add(benchmarkSecurity);

        // Point user's benchmark at the foreign security
        user.SetReturnAnalysisSettings(benchmarkSecurity.Id, false, 0m);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        BenchmarkComparisonDto? result = await _sut.GetBenchmarkComparisonAsync(security.Id, user.Id, CancellationToken.None);

        // Assert
        result.Should().BeNull();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // UpdateUserSettingsAsync
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// When a benchmark security ID is provided but the security belongs to a different user,
    /// the method must throw <see cref="ArgumentException"/> (S-3 ownership enforcement).
    /// </summary>
    [Fact]
    public async Task UpdateUserSettingsAsync_Should_ThrowArgumentException_When_BenchmarkSecurityNotOwnedByUser()
    {
        // Arrange – foreign security not owned by the requesting user
        var (_, user) = SetupSecurityAndUser();
        var otherOwner = Guid.NewGuid();
        var foreignSecurity = new Security(otherOwner, "Foreign ETF", "FOR", null, null, "USD", null);
        _db.Securities.Add(foreignSecurity);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        Func<Task> act = async () => await _sut.UpdateUserSettingsAsync(
            user.Id, foreignSecurity.Id, false, 0.02m, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>();
    }

    /// <summary>
    /// When benchmarkSecurityId is null, the existing benchmark setting is cleared without error.
    /// </summary>
    [Fact]
    public async Task UpdateUserSettingsAsync_Should_ClearBenchmark_When_BenchmarkIdIsNull()
    {
        // Arrange – user with an existing benchmark security
        var (security, user) = SetupSecurityAndUser();
        user.SetReturnAnalysisSettings(security.Id, false, 0m);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        await _sut.UpdateUserSettingsAsync(user.Id, null, false, 0m, CancellationToken.None);

        // Assert – reload the user entity and verify the benchmark was cleared
        await _db.Entry(user).ReloadAsync(TestContext.Current.CancellationToken);
        user.BenchmarkSecurityId.Should().BeNull();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // InvalidateCacheAsync
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// InvalidateCacheAsync must pass a token that contains both the securityId and userId
    /// to the cache's InvalidateAsync method, so all related entries are evicted.
    /// </summary>
    [Fact]
    public async Task InvalidateCacheAsync_Should_InvalidateCorrectToken_When_CalledWithValidIds()
    {
        // Arrange – use a mock cache to capture the invalidation token
        var cacheMock = new Mock<IReturnAnalysisCache>();
        cacheMock.Setup(c => c.InvalidateAsync(It.IsAny<string>())).Returns(Task.CompletedTask);

        var sut = new ReturnAnalysisService(
            _db,
            _calcMock.Object,
            _fifoMock.Object,
            cacheMock.Object,
            NullLogger<ReturnAnalysisService>.Instance,
            new TestReturnAnalysisLocalizer());

        var secId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        // Act
        await sut.InvalidateCacheAsync(secId, userId);

        // Assert – the token passed to InvalidateAsync must contain both IDs
        cacheMock.Verify(
            c => c.InvalidateAsync(
                It.Is<string>(s => s.Contains(secId.ToString()) && s.Contains(userId.ToString()))),
            Times.Once);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GetDetailedMetricsAsync — Sharpe Ratio opt-in
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// When the user has ShowSharpeRatio = false, the Sharpe Ratio must not be computed
    /// and the result must be null (opt-in guard FR-8).
    /// </summary>
    [Fact]
    public async Task GetDetailedMetricsAsync_Should_ReturnNullSharpeRatio_When_ShowSharpeRatioIsFalse()
    {
        // Arrange
        var (security, user) = SetupSecurityAndUser();
        user.SetReturnAnalysisSettings(null, showSharpeRatio: false, riskFreeRate: 0.02m);

        var posting = CreateBuyPosting(security.Id, DateTime.Today.AddDays(-10), -1_000m, 10m);
        _db.Postings.Add(posting);
        _db.SecurityPrices.Add(new SecurityPrice(security.Id, DateTime.Today.AddDays(-1), 105m));
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Setup TWR and volatility so the Sharpe guard would fire — if opt-in were true
        _calcMock.Setup(c => c.CalculateTwr(It.IsAny<IReadOnlyList<TwrPeriodInput>>())).Returns(0.05m);
        _calcMock.Setup(c => c.CalculateVolatility(It.IsAny<IReadOnlyList<decimal>>())).Returns(0.15m);

        // Act
        DetailedReturnMetricsDto? result = await _sut.GetDetailedMetricsAsync(security.Id, user.Id, CancellationToken.None);

        // Assert – Sharpe Ratio must be null, and the calculation must never be invoked
        result.Should().NotBeNull();
        result!.SharpeRatio.Should().BeNull();
        _calcMock.Verify(
            c => c.CalculateSharpeRatio(It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<decimal>()),
            Times.Never);
    }

    /// <summary>
    /// When volatility is zero (flat price series), the Sharpe Ratio must remain null
    /// to avoid division by zero, even when ShowSharpeRatio is enabled.
    /// </summary>
    [Fact]
    public async Task GetDetailedMetricsAsync_Should_ReturnNullSharpeRatio_When_VolatilityIsZero()
    {
        // Arrange
        var (security, user) = SetupSecurityAndUser();
        user.SetReturnAnalysisSettings(null, showSharpeRatio: true, riskFreeRate: 0.02m);

        var posting = CreateBuyPosting(security.Id, DateTime.Today.AddDays(-10), -1_000m, 10m);
        _db.Postings.Add(posting);
        _db.SecurityPrices.Add(new SecurityPrice(security.Id, DateTime.Today.AddDays(-1), 105m));
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        _calcMock.Setup(c => c.CalculateTwr(It.IsAny<IReadOnlyList<TwrPeriodInput>>())).Returns(0.05m);
        _calcMock.Setup(c => c.CalculateVolatility(It.IsAny<IReadOnlyList<decimal>>())).Returns(0m); // zero volatility

        // Act
        DetailedReturnMetricsDto? result = await _sut.GetDetailedMetricsAsync(security.Id, user.Id, CancellationToken.None);

        // Assert – division by zero must be guarded; Sharpe Ratio stays null
        result.Should().NotBeNull();
        result!.SharpeRatio.Should().BeNull();
        _calcMock.Verify(
            c => c.CalculateSharpeRatio(It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<decimal>()),
            Times.Never);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GetKpiBreakdownsAsync
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// When the security does not exist or does not belong to the requesting user,
    /// the method must return null (user-scoping S-1).
    /// </summary>
    [Fact]
    public async Task GetKpiBreakdownsAsync_Should_ReturnNull_When_SecurityNotFound()
    {
        // Arrange – security exists but belongs to a different user
        var (security, _) = SetupSecurityAndUser();
        var otherUserId = Guid.NewGuid();

        // Act
        IReadOnlyList<KpiBreakdownDto>? result = await _sut.GetKpiBreakdownsAsync(security.Id, otherUserId, CancellationToken.None);

        // Assert
        result.Should().BeNull();
    }

    /// <summary>
    /// When the security is owned by the user but has no transactions,
    /// the method must return null (no data to break down).
    /// </summary>
    [Fact]
    public async Task GetKpiBreakdownsAsync_Should_ReturnNull_When_NoTransactions()
    {
        // Arrange
        var (security, user) = SetupSecurityAndUser();

        // Act
        IReadOnlyList<KpiBreakdownDto>? result = await _sut.GetKpiBreakdownsAsync(security.Id, user.Id, CancellationToken.None);

        // Assert
        result.Should().BeNull();
    }

    /// <summary>
    /// When there are transactions, the method must return a non-null list
    /// containing breakdowns for all widget KPIs (TotalReturn, MarketValue, InvestedCapital, Cagr, Irr).
    /// </summary>
    [Fact]
    public async Task GetKpiBreakdownsAsync_Should_ReturnAllKpis_When_TransactionsExist()
    {
        // Arrange – one buy posting
        var (security, user) = SetupSecurityAndUser();
        var posting = CreateBuyPosting(security.Id, DateTime.Today.AddDays(-400), -1_000m, 10m);
        _db.Postings.Add(posting);
        _db.SecurityPrices.Add(new SecurityPrice(security.Id, DateTime.Today, 110m));
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        IReadOnlyList<KpiBreakdownDto>? result = await _sut.GetKpiBreakdownsAsync(security.Id, user.Id, CancellationToken.None);

        // Assert – all five KPI breakdowns must be present
        result.Should().NotBeNull();
        result!.Select(b => b.KpiKey)
            .Should().BeEquivalentTo(["TotalReturn", "MarketValue", "InvestedCapital", "Cagr", "Irr"]);
    }

    /// <summary>
    /// Each returned breakdown must have a non-empty display name, formula text, and description.
    /// </summary>
    [Fact]
    public async Task GetKpiBreakdownsAsync_Should_PopulateMetadata_For_EachKpi()
    {
        // Arrange
        var (security, user) = SetupSecurityAndUser();
        var posting = CreateBuyPosting(security.Id, DateTime.Today.AddDays(-30), -1_000m, 10m);
        _db.Postings.Add(posting);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        IReadOnlyList<KpiBreakdownDto>? result = await _sut.GetKpiBreakdownsAsync(security.Id, user.Id, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.Should().AllSatisfy(b =>
        {
            b.DisplayName.Should().NotBeNullOrEmpty();
            b.FormulaText.Should().NotBeNullOrEmpty();
            b.Description.Should().NotBeNullOrEmpty();
            b.Groups.Should().NotBeNull();
        });
    }

    /// <summary>
    /// The InvestedCapital breakdown must contain a "Käufe" group with one item per buy posting
    /// and the group total must equal the sum of all buy amounts.
    /// </summary>
    [Fact]
    public async Task GetKpiBreakdownsAsync_Should_IncludeBuyPostings_In_InvestedCapitalBreakdown()
    {
        // Arrange – two buy postings at different dates
        var (security, user) = SetupSecurityAndUser();
        var buy1 = CreateBuyPosting(security.Id, DateTime.Today.AddDays(-200), -500m, 5m);
        var buy2 = CreateBuyPosting(security.Id, DateTime.Today.AddDays(-100), -600m, 6m);
        _db.Postings.AddRange(buy1, buy2);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Configure FIFO mock to return two remaining lots matching the two buys
        _fifoMock
            .Setup(f => f.Calculate(It.IsAny<IReadOnlyList<SecurityTransaction>>()))
            .Returns(new FifoCostBasisResult(1_100m, 0m,
                new FifoLot[] { new(DateTime.Today.AddDays(-200), 5m, 100m), new(DateTime.Today.AddDays(-100), 6m, 100m) },
                11m, false, null, 0m));

        // Act
        IReadOnlyList<KpiBreakdownDto>? result = await _sut.GetKpiBreakdownsAsync(security.Id, user.Id, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        var ic = result!.First(b => b.KpiKey == "InvestedCapital");
        var fifoGroup = ic.Groups.FirstOrDefault(g => g.GroupName == "FIFO-Kostenbasis (gehaltene Anteile)");

        fifoGroup.Should().NotBeNull();
        fifoGroup!.Items.Should().HaveCount(2, "one item per remaining FIFO lot");
        fifoGroup.GroupTotal.Should().Be(1_100m); // 5*100 + 6*100
    }

    /// <summary>
    /// The TotalReturn breakdown must include a "Dividenden (netto)" group (Group[2])
    /// containing each dividend posting with the correct amount.
    /// </summary>
    [Fact]
    public async Task GetKpiBreakdownsAsync_Should_IncludeDividends_In_TotalReturnBreakdown()
    {
        // Arrange – one buy and one dividend posting
        var (security, user) = SetupSecurityAndUser();
        var buy = CreateBuyPosting(security.Id, DateTime.Today.AddDays(-200), -1_000m, 10m);
        var dividend = new Posting(
            Guid.NewGuid(),
            PostingKind.Security,
            null, null, null,
            security.Id,
            DateTime.Today.AddDays(-30),
            50m,
            null, null, null,
            SecurityPostingSubType.Dividend,
            null);
        _db.Postings.AddRange(buy, dividend);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        IReadOnlyList<KpiBreakdownDto>? result = await _sut.GetKpiBreakdownsAsync(security.Id, user.Id, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        var tr = result!.First(b => b.KpiKey == "TotalReturn");
        var dividendsGroup = tr.Groups.FirstOrDefault(g => g.GroupName == "Dividenden (netto)");

        dividendsGroup.Should().NotBeNull("group 'Dividenden (netto)' must be present in TotalReturn");
        dividendsGroup!.Items.Should().HaveCount(1);
        dividendsGroup.Items[0].Amount.Should().Be(50m);
        dividendsGroup.GroupTotal.Should().Be(50m, "netDividends = 50 Dividende − 0 Steuern");
    }

    /// <summary>
    /// The TotalReturn breakdown must use the new four-group structure:
    /// [0] "Aktueller Marktwert" (positiv), [1] "Investiertes Kapital" (negativ),
    /// [2] "Dividenden (netto)" (positiv), [3] "Gesamtrendite" (Ergebnis).
    /// Group[0] must have one item with the current market value.
    /// Group[1] must have the buy items negated (outflows).
    /// </summary>
    [Fact]
    public async Task GetKpiBreakdownsAsync_Should_BreakDownTotalReturn_Into_FourGroups()
    {
        // Arrange – one buy posting, price available
        var (security, user) = SetupSecurityAndUser();
        var buy = CreateBuyPosting(security.Id, DateTime.Today.AddDays(-200), -1_000m, 10m);
        _db.Postings.Add(buy);
        _db.SecurityPrices.Add(new SecurityPrice(security.Id, DateTime.Today, 120m)); // 10 shares × 120 = 1 200 market value
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // FIFO mock: cost basis 1 000, 10 shares held
        _fifoMock
            .Setup(f => f.Calculate(It.IsAny<IReadOnlyList<SecurityTransaction>>()))
            .Returns(new FifoCostBasisResult(1_000m, 0m, Array.Empty<FifoLot>(), 10m, false, null, 0m));

        // Act
        IReadOnlyList<KpiBreakdownDto>? result = await _sut.GetKpiBreakdownsAsync(security.Id, user.Id, CancellationToken.None);

        // Assert – group order: [0] Marktwert, [1] Investiertes Kapital, [2] Dividenden (netto), [3] Gesamtrendite
        result.Should().NotBeNull();
        var tr = result!.First(b => b.KpiKey == "TotalReturn");

        tr.Groups.Should().HaveCount(4, "TotalReturn must have exactly four formula groups");
        tr.Groups[0].GroupName.Should().Be("Aktueller Marktwert");
        tr.Groups[1].GroupName.Should().Be("Investiertes Kapital");
        tr.Groups[2].GroupName.Should().Be("Dividenden (netto)");
        tr.Groups[3].GroupName.Should().Be("Gesamtrendite");

        // Group[0]: Aktueller Marktwert – single item = 10 × 120 = 1 200
        var marktwertGroup = tr.Groups[0];
        marktwertGroup.IsPositiveContribution.Should().BeTrue();
        marktwertGroup.Items.Should().HaveCount(1, "exactly one item: current market value");
        marktwertGroup.Items[0].Amount.Should().Be(1_200m, "market value = 10 shares × 120");
        marktwertGroup.Items[0].Note.Should().Contain("Anteile", "note must reference share count and price");
        marktwertGroup.GroupTotal.Should().Be(1_200m);

        // Group[1]: Investiertes Kapital – buy item negated (outflow)
        var icGroup = tr.Groups[1];
        icGroup.IsPositiveContribution.Should().BeFalse();
        icGroup.Items.Should().HaveCount(1, "one item per buy posting");
        icGroup.Items[0].Amount.Should().Be(-1_000m, "invested capital must appear as a negative outflow");
        icGroup.Items[0].Note.Should().Contain("Anteile", "note must reference the number of shares and price per share");
        icGroup.GroupTotal.Should().Be(-1_000m, "GroupTotal = −investedCapital");

        // Group[2]: Dividenden (netto) – no dividends in this test
        tr.Groups[2].Items.Should().BeEmpty("no dividend postings were created");
        tr.Groups[2].GroupTotal.Should().Be(0m);

        // Group[3]: Gesamtrendite – totalReturnAbsolute = 1200 − 1000 + 0 = 200
        var resultGroup = tr.Groups[3];
        resultGroup.Items.Should().HaveCount(1, "one summary item for total return");
        resultGroup.Items[0].Amount.Should().Be(200m, "total return = market value − invested capital + net dividends");
        resultGroup.IsPositiveContribution.Should().BeTrue("positive total return");
        resultGroup.GroupTotal.Should().Be(200m);

        // GroupTotalPercent must be set so the info panel can show both % and € side by side.
        // The mock returns 0.05 m for CalculateTotalReturn (see constructor setup).
        resultGroup.GroupTotalPercent.Should().Be(0.05m,
            "Group[3] must carry the percentage return so the info-panel can render '▲+5.00 % (200,00 EUR)'");
    }

    /// <summary>
    /// Fees in TotalReturn must appear inside Group[1] "Investiertes Kapital" as negated items
    /// (standalone fees with note "Gebühr (ohne Kaufzuordnung)"), not in a separate fees group.
    /// This is a regression test ensuring individual fee rows are always visible in the info panel.
    /// </summary>
    [Fact]
    public async Task GetKpiBreakdownsAsync_Should_PopulateItems_For_FeesGroup()
    {
        // Arrange – one buy and two fee postings (standalone, no GroupId)
        var (security, user) = SetupSecurityAndUser();
        var buy = CreateBuyPosting(security.Id, DateTime.Today.AddDays(-200), -1_000m, 10m);
        var fee1 = new Posting(
            Guid.NewGuid(),
            PostingKind.Security,
            null, null, null,
            security.Id,
            DateTime.Today.AddDays(-100),
            -4m,
            null, null, null,
            SecurityPostingSubType.Fee,
            null);
        var fee2 = new Posting(
            Guid.NewGuid(),
            PostingKind.Security,
            null, null, null,
            security.Id,
            DateTime.Today.AddDays(-50),
            -5.64m,
            null, null, null,
            SecurityPostingSubType.Fee,
            null);
        _db.Postings.AddRange(buy, fee1, fee2);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        IReadOnlyList<KpiBreakdownDto>? result = await _sut.GetKpiBreakdownsAsync(security.Id, user.Id, CancellationToken.None);

        // Assert – TotalReturn Group[1] "Investiertes Kapital" must contain negated items for each fee
        result.Should().NotBeNull();
        var tr = result!.First(b => b.KpiKey == "TotalReturn");

        // Verify group order
        tr.Groups[0].GroupName.Should().Be("Aktueller Marktwert");
        tr.Groups[1].GroupName.Should().Be("Investiertes Kapital");
        tr.Groups[2].GroupName.Should().Be("Dividenden (netto)");
        tr.Groups[3].GroupName.Should().Be("Gesamtrendite");

        // Group[1]: one buy + two standalone fees = 3 items, all negated
        var icGroup = tr.Groups[1];
        icGroup.IsPositiveContribution.Should().BeFalse("invested capital is a negative contribution to total return");
        icGroup.Items.Should().HaveCount(3, "one buy item + two fee items must each appear separately");

        // buy item (index 0)
        icGroup.Items[0].Amount.Should().Be(-1_000m, "buy amount must be negated (outflow)");

        // fee1 (index 1) – standalone, appears after the buy
        icGroup.Items[1].Amount.Should().Be(-4m, "first fee amount must be negated (outflow)");
        icGroup.Items[1].Date.Should().Be(DateTime.Today.AddDays(-100));
        icGroup.Items[1].Note.Should().Be("Gebühr (ohne Kaufzuordnung)");

        // fee2 (index 2)
        icGroup.Items[2].Amount.Should().Be(-5.64m, "second fee amount must be negated (outflow)");
        icGroup.Items[2].Date.Should().Be(DateTime.Today.AddDays(-50));
        icGroup.Items[2].Note.Should().Be("Gebühr (ohne Kaufzuordnung)");

        // Regression: Items must never be empty when fee postings exist
        icGroup.Items.Should().NotBeEmpty("Items must be populated – not just GroupTotal");

        // No separate fees group in TotalReturn any more
        tr.Groups.Should().NotContain(g => g.GroupName == "Gebühren (Abzug)",
            "fees are now embedded in 'Investiertes Kapital', not a standalone group");
    }

    /// <summary>
    /// The InvestedCapital and TotalReturn breakdowns must list each buy posting and each linked fee
    /// (same GroupId) as separate items. Fees with a GroupId that does not match any buy (standalone)
    /// must be appended at the end of the "Käufe" group with the note "Gebühr (ohne Kaufzuordnung)".
    /// </summary>
    [Fact]
    public async Task GetKpiBreakdownsAsync_Should_ListEachBuyAndFeeAsSeperateItem_In_InvestedCapitalBreakdown()
    {
        // Arrange – two buys, one fee linked to the first buy via GroupId, one standalone fee
        var (security, user) = SetupSecurityAndUser();

        var groupId1 = Guid.NewGuid(); // shared by buy1 + fee1 (linked)
        var groupId2 = Guid.NewGuid(); // used only by buy2 (no linked fee)
        var standaloneGroupId = Guid.NewGuid(); // no matching buy → standalone fee

        var buy1 = CreateBuyPosting(security.Id, DateTime.Today.AddDays(-200), -500m, 5m);
        buy1.SetGroup(groupId1);

        var fee1 = new Posting(
            Guid.NewGuid(),
            PostingKind.Security,
            null, null, null,
            security.Id,
            DateTime.Today.AddDays(-200),
            -10m,
            null, null, null,
            SecurityPostingSubType.Fee,
            null);
        fee1.SetGroup(groupId1);

        var buy2 = CreateBuyPosting(security.Id, DateTime.Today.AddDays(-100), -600m, 6m);
        buy2.SetGroup(groupId2);

        var standaloneFee = new Posting(
            Guid.NewGuid(),
            PostingKind.Security,
            null, null, null,
            security.Id,
            DateTime.Today.AddDays(-50),
            -5m,
            null, null, null,
            SecurityPostingSubType.Fee,
            null);
        standaloneFee.SetGroup(standaloneGroupId);

        _db.Postings.AddRange(buy1, fee1, buy2, standaloneFee);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // FIFO: two remaining lots (buy1 absorbs its linked fee in CostPerUnit), standalone fee in StandaloneFeeTotal
        _fifoMock
            .Setup(f => f.Calculate(It.IsAny<IReadOnlyList<SecurityTransaction>>()))
            .Returns(new FifoCostBasisResult(1_115m, 0m,
                new FifoLot[] { new(DateTime.Today.AddDays(-200), 5m, 102m), new(DateTime.Today.AddDays(-100), 6m, 100m) },
                11m, false, null, 5m));

        // Act
        IReadOnlyList<KpiBreakdownDto>? result = await _sut.GetKpiBreakdownsAsync(security.Id, user.Id, CancellationToken.None);

        // Assert – InvestedCapital "FIFO-Kostenbasis" group (remaining lots)
        result.Should().NotBeNull();
        var ic = result!.First(b => b.KpiKey == "InvestedCapital");
        var fifoGroup = ic.Groups.First(g => g.GroupName == "FIFO-Kostenbasis (gehaltene Anteile)");

        fifoGroup.Items.Should().HaveCount(2, "one item per remaining FIFO lot");

        // lot1: 5 shares at 102 (buy + linked fee embedded) = 510
        fifoGroup.Items[0].Date.Should().Be(DateTime.Today.AddDays(-200));
        fifoGroup.Items[0].Amount.Should().Be(510m, "lot1: 5 × 102");
        fifoGroup.Items[0].Note.Should().Contain("Anteile");

        // lot2: 6 shares at 100 = 600
        fifoGroup.Items[1].Date.Should().Be(DateTime.Today.AddDays(-100));
        fifoGroup.Items[1].Amount.Should().Be(600m, "lot2: 6 × 100");

        fifoGroup.GroupTotal.Should().Be(1_110m, "displayInvestedCapital = TotalCostBasis(1115) − StandaloneFeeTotal(5)");

        // Assert – TotalReturn Group[1] "Investiertes Kapital" (same items but negated; market value is now Group[0])
        var tr = result.First(b => b.KpiKey == "TotalReturn");

        // Verify group order: [0] Marktwert, [1] Investiertes Kapital, [2] Dividenden (netto), [3] Gesamtrendite
        tr.Groups[0].GroupName.Should().Be("Aktueller Marktwert");
        tr.Groups[1].GroupName.Should().Be("Investiertes Kapital");
        tr.Groups[2].GroupName.Should().Be("Dividenden (netto)");
        tr.Groups[3].GroupName.Should().Be("Gesamtrendite");

        var icGroupTr = tr.Groups[1];
        icGroupTr.IsPositiveContribution.Should().BeFalse();
        icGroupTr.Items.Should().HaveCount(4,
            "two buys + one linked fee + one standalone fee must each appear as a negative item");

        icGroupTr.Items[0].Amount.Should().Be(-500m, "buy1 must appear as negative outflow");
        icGroupTr.Items[1].Amount.Should().Be(-10m, "linked fee must appear as negative outflow");
        icGroupTr.Items[2].Amount.Should().Be(-600m, "buy2 must appear as negative outflow");
        icGroupTr.Items[3].Amount.Should().Be(-5m, "standalone fee must appear as negative outflow");

        // Group[0]: current market value as separate positive group
        tr.Groups[0].Items.Should().HaveCount(1, "exactly one item: current market value");
        tr.Groups[0].Items[0].Amount.Should().BeGreaterThanOrEqualTo(0m, "market value must be positive");
        tr.Groups[0].Items[0].Note.Should().Contain("Anteile", "note must reference share count and price");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // CAGR Breakdown — GetKpiBreakdownsAsync
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The CAGR breakdown must contain exactly 5 groups in the correct order:
    /// Marktwert, Nettodividenden, Investiertes Kapital, Anlagedauer, CAGR (Ergebnis).
    /// </summary>
    [Fact]
    public async Task GetKpiBreakdownsAsync_CagrBreakdown_Should_HaveExactlyFiveGroups_In_CorrectOrder()
    {
        // Arrange – one buy posting > 1 year ago so CAGR is computable
        var (security, user) = SetupSecurityAndUser();
        var buy = CreateBuyPosting(security.Id, DateTime.Today.AddDays(-400), -1_000m, 10m);
        _db.Postings.Add(buy);
        _db.SecurityPrices.Add(new SecurityPrice(security.Id, DateTime.Today, 120m));
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        _fifoMock
            .Setup(f => f.Calculate(It.IsAny<IReadOnlyList<SecurityTransaction>>()))
            .Returns(new FifoCostBasisResult(1_000m, 0m, Array.Empty<FifoLot>(), 10m, false, null, 0m));

        // Act
        IReadOnlyList<KpiBreakdownDto>? result = await _sut.GetKpiBreakdownsAsync(security.Id, user.Id, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        var cagr = result!.First(b => b.KpiKey == "Cagr");

        cagr.Groups.Should().HaveCount(5, "CAGR breakdown must have exactly 5 formula groups");
        cagr.Groups[0].GroupName.Should().Be("Marktwert");
        cagr.Groups[1].GroupName.Should().Be("Nettodividenden (Dividenden − Steuern)");
        cagr.Groups[2].GroupName.Should().Be("Investiertes Kapital");
        cagr.Groups[3].GroupName.Should().Be("Anlagedauer");
        cagr.Groups[4].GroupName.Should().Be("CAGR (Ergebnis)");
    }

    /// <summary>
    /// Group[0] "Marktwert" must contain exactly one item representing
    /// total shares held × current price – identical to TotalReturn's Marktwert group.
    /// </summary>
    [Fact]
    public async Task GetKpiBreakdownsAsync_CagrBreakdown_Group0_Should_ContainMarktwertItems()
    {
        // Arrange – two buy postings; FIFO reports 11 shares held, price = 110 → market value = 1210
        var (security, user) = SetupSecurityAndUser();
        var buy1 = CreateBuyPosting(security.Id, DateTime.Today.AddDays(-400), -500m, 5m);
        var buy2 = CreateBuyPosting(security.Id, DateTime.Today.AddDays(-300), -600m, 6m);
        _db.Postings.AddRange(buy1, buy2);
        _db.SecurityPrices.Add(new SecurityPrice(security.Id, DateTime.Today, 110m)); // current price = 110
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        _fifoMock
            .Setup(f => f.Calculate(It.IsAny<IReadOnlyList<SecurityTransaction>>()))
            .Returns(new FifoCostBasisResult(1_100m, 0m, Array.Empty<FifoLot>(), 11m, false, null, 0m));

        // Act
        IReadOnlyList<KpiBreakdownDto>? result = await _sut.GetKpiBreakdownsAsync(security.Id, user.Id, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        var cagrGroup0 = result!.First(b => b.KpiKey == "Cagr").Groups[0];

        cagrGroup0.GroupName.Should().Be("Marktwert");
        cagrGroup0.IsPositiveContribution.Should().BeTrue();
        cagrGroup0.Items.Should().HaveCount(1, "exactly one item: total shares held × current price");

        // Single item: 11 shares × 110 = 1210
        cagrGroup0.Items[0].Amount.Should().Be(11m * 110m, "11 shares held × 110 EUR current price");
        cagrGroup0.Items[0].Note.Should().Contain("Anteile", "note must reference share count and current price");
    }

    /// <summary>
    /// Group[1] "Nettodividenden" must contain dividend items and tax items (negative).
    /// The GroupTotal must equal gross dividends minus taxes.
    /// </summary>
    [Fact]
    public async Task GetKpiBreakdownsAsync_CagrBreakdown_Group1_Should_ContainNetDividendItems()
    {
        // Arrange
        var (security, user) = SetupSecurityAndUser();
        var groupId = Guid.NewGuid();

        var buy = CreateBuyPosting(security.Id, DateTime.Today.AddDays(-400), -1_000m, 10m);
        var dividend = new Posting(
            Guid.NewGuid(), PostingKind.Security,
            null, null, null, security.Id,
            DateTime.Today.AddDays(-100), 80m,
            null, null, null, SecurityPostingSubType.Dividend, null);
        dividend.SetGroup(groupId);

        var tax = new Posting(
            Guid.NewGuid(), PostingKind.Security,
            null, null, null, security.Id,
            DateTime.Today.AddDays(-100), -20m,
            null, null, null, SecurityPostingSubType.Tax, null);
        tax.SetGroup(groupId);

        _db.Postings.AddRange(buy, dividend, tax);
        _db.SecurityPrices.Add(new SecurityPrice(security.Id, DateTime.Today, 120m));
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        _fifoMock
            .Setup(f => f.Calculate(It.IsAny<IReadOnlyList<SecurityTransaction>>()))
            .Returns(new FifoCostBasisResult(1_000m, 0m, Array.Empty<FifoLot>(), 10m, false, null, 0m));

        // Act
        IReadOnlyList<KpiBreakdownDto>? result = await _sut.GetKpiBreakdownsAsync(security.Id, user.Id, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        var cagrGroup1 = result!.First(b => b.KpiKey == "Cagr").Groups[1];

        cagrGroup1.GroupName.Should().Be("Nettodividenden (Dividenden − Steuern)");
        cagrGroup1.Items.Should().HaveCount(2, "one dividend item + one linked tax item");

        // Dividend item
        cagrGroup1.Items[0].Amount.Should().Be(80m, "gross dividend amount");

        // Tax item (negative)
        cagrGroup1.Items[1].Amount.Should().Be(-20m, "tax is a negative deduction");
        cagrGroup1.Items[1].Note.Should().Be("Steuer");

        cagrGroup1.GroupTotal.Should().Be(60m, "netDividends = 80 − 20 = 60");
    }

    /// <summary>
    /// Group[2] "Investiertes Kapital" must contain the buy items (positive amounts) and their fees.
    /// The GroupTotal must equal the total cost basis (investedCapital).
    /// </summary>
    [Fact]
    public async Task GetKpiBreakdownsAsync_CagrBreakdown_Group2_Should_ContainInvestedCapitalItems()
    {
        // Arrange – two buy postings
        var (security, user) = SetupSecurityAndUser();
        var buy1 = CreateBuyPosting(security.Id, DateTime.Today.AddDays(-400), -500m, 5m);
        var buy2 = CreateBuyPosting(security.Id, DateTime.Today.AddDays(-300), -600m, 6m);
        _db.Postings.AddRange(buy1, buy2);
        _db.SecurityPrices.Add(new SecurityPrice(security.Id, DateTime.Today, 110m));
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        _fifoMock
            .Setup(f => f.Calculate(It.IsAny<IReadOnlyList<SecurityTransaction>>()))
            .Returns(new FifoCostBasisResult(1_100m, 0m, Array.Empty<FifoLot>(), 11m, false, null, 0m));

        // Act
        IReadOnlyList<KpiBreakdownDto>? result = await _sut.GetKpiBreakdownsAsync(security.Id, user.Id, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        var cagrGroup2 = result!.First(b => b.KpiKey == "Cagr").Groups[2];

        cagrGroup2.GroupName.Should().Be("Investiertes Kapital");
        cagrGroup2.IsPositiveContribution.Should().BeFalse("invested capital is a cost, hence negative contribution");
        cagrGroup2.Items.Should().HaveCount(2, "one item per buy posting");
        cagrGroup2.Items[0].Amount.Should().Be(500m, "buy1 amount positive");
        cagrGroup2.Items[1].Amount.Should().Be(600m, "buy2 amount positive");
        cagrGroup2.GroupTotal.Should().Be(1_100m, "GroupTotal = investedCapital");
    }

    /// <summary>
    /// Group[3] "Anlagedauer" must have a non-null GroupNote with the holding period text
    /// and must contain three text-only items (Erster Kauf, Heute, Zeitraum).
    /// </summary>
    [Fact]
    public async Task GetKpiBreakdownsAsync_CagrBreakdown_Group3_Should_HaveGroupNoteWithDuration()
    {
        // Arrange – buy > 1 year ago so duration is meaningful
        var (security, user) = SetupSecurityAndUser();
        var firstBuyDate = DateTime.Today.AddDays(-400);
        var buy = CreateBuyPosting(security.Id, firstBuyDate, -1_000m, 10m);
        _db.Postings.Add(buy);
        _db.SecurityPrices.Add(new SecurityPrice(security.Id, DateTime.Today, 120m));
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        _fifoMock
            .Setup(f => f.Calculate(It.IsAny<IReadOnlyList<SecurityTransaction>>()))
            .Returns(new FifoCostBasisResult(1_000m, 0m, Array.Empty<FifoLot>(), 10m, false, null, 0m));

        // Act
        IReadOnlyList<KpiBreakdownDto>? result = await _sut.GetKpiBreakdownsAsync(security.Id, user.Id, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        var cagrGroup3 = result!.First(b => b.KpiKey == "Cagr").Groups[3];

        cagrGroup3.GroupName.Should().Be("Anlagedauer");
        cagrGroup3.GroupNote.Should().NotBeNullOrEmpty("GroupNote must contain the human-readable duration");
        cagrGroup3.GroupNote.Should().Contain("Jahr", "duration text must mention years");

        cagrGroup3.Items.Should().HaveCount(3, "items: Erster Kauf, Heute, Zeitraum");
        cagrGroup3.Items[0].Note.Should().Be("Erster Kauf");
        cagrGroup3.Items[0].Date.Should().Be(firstBuyDate);
        cagrGroup3.Items[1].Note.Should().Be("Heute");
        cagrGroup3.Items[1].Date.Should().Be(DateTime.Today);
        cagrGroup3.Items[2].Note.Should().Contain("Zeitraum", "third item must describe the time span");
    }

    /// <summary>
    /// Group[4] "CAGR (Ergebnis)" must have GroupTotalPercent set to the computed CAGR value
    /// and must have no items (result-only group).
    /// </summary>
    [Fact]
    public async Task GetKpiBreakdownsAsync_CagrBreakdown_Group4_Should_HaveGroupTotalPercent()
    {
        // Arrange – buy > 1 year ago; mock returns 0.12 for CAGR
        var (security, user) = SetupSecurityAndUser();
        var buy = CreateBuyPosting(security.Id, DateTime.Today.AddDays(-400), -1_000m, 10m);
        _db.Postings.Add(buy);
        _db.SecurityPrices.Add(new SecurityPrice(security.Id, DateTime.Today, 120m));
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        _fifoMock
            .Setup(f => f.Calculate(It.IsAny<IReadOnlyList<SecurityTransaction>>()))
            .Returns(new FifoCostBasisResult(1_000m, 0m, Array.Empty<FifoLot>(), 10m, false, null, 0m));

        _calcMock
            .Setup(c => c.CalculateCagr(It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<double>()))
            .Returns(0.12m);

        // Act
        IReadOnlyList<KpiBreakdownDto>? result = await _sut.GetKpiBreakdownsAsync(security.Id, user.Id, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        var cagrGroup4 = result!.First(b => b.KpiKey == "Cagr").Groups[4];

        cagrGroup4.GroupName.Should().Be("CAGR (Ergebnis)");
        cagrGroup4.GroupTotalPercent.Should().Be(0.12m, "GroupTotalPercent must carry the CAGR decimal value");
        cagrGroup4.Items.Should().BeEmpty("result group has no individual items");
    }

    /// <summary>
    /// Edge case: when the holding period in years is 0 (today is the first buy day),
    /// the CAGR result group must have GroupTotalPercent = 0 (not null, not error).
    /// </summary>
    [Fact]
    public async Task GetKpiBreakdownsAsync_CagrBreakdown_Group4_Should_ReturnZero_When_YearsIsZero()
    {
        // Arrange – buy today (years = 0)
        var (security, user) = SetupSecurityAndUser();
        var buy = CreateBuyPosting(security.Id, DateTime.Today, -1_000m, 10m);
        _db.Postings.Add(buy);
        _db.SecurityPrices.Add(new SecurityPrice(security.Id, DateTime.Today, 120m));
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        _fifoMock
            .Setup(f => f.Calculate(It.IsAny<IReadOnlyList<SecurityTransaction>>()))
            .Returns(new FifoCostBasisResult(1_000m, 0m, Array.Empty<FifoLot>(), 10m, false, null, 0m));

        // Act
        IReadOnlyList<KpiBreakdownDto>? result = await _sut.GetKpiBreakdownsAsync(security.Id, user.Id, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        var cagrGroup4 = result!.First(b => b.KpiKey == "Cagr").Groups[4];

        cagrGroup4.GroupTotalPercent.Should().Be(0m,
            "when holding period is 0 years, CAGR must be 0 (edge case guard)");

        // CalculateCagr must NOT be called because cagrYears <= 0
        _calcMock.Verify(
            c => c.CalculateCagr(It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<double>()),
            Times.Never,
            "CalculateCagr must be skipped when years <= 0");
    }

    /// <summary>
    /// Edge case: when there are no buy transactions (historicalInvestedCapital = 0),
    /// the CAGR result group must have GroupTotalPercent = 0 (no division by zero).
    /// With no buys, firstBuyDate = Today → cagrYears = 0, which also triggers the guard.
    /// </summary>
    [Fact]
    public async Task GetKpiBreakdownsAsync_CagrBreakdown_Group4_Should_ReturnZero_When_InvestedCapitalIsZero()
    {
        // Arrange – only a dividend posting (no buy) so historicalInvestedCapital = 0
        var (security, user) = SetupSecurityAndUser();
        var dividend = new Posting(
            Guid.NewGuid(),
            PostingKind.Security,
            null, null, null,
            security.Id,
            DateTime.Today.AddDays(-400),
            50m,
            null, null, null,
            SecurityPostingSubType.Dividend,
            null);
        _db.Postings.Add(dividend);
        _db.SecurityPrices.Add(new SecurityPrice(security.Id, DateTime.Today, 120m));
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        _fifoMock
            .Setup(f => f.Calculate(It.IsAny<IReadOnlyList<SecurityTransaction>>()))
            .Returns(new FifoCostBasisResult(0m, 0m, Array.Empty<FifoLot>(), 0m, false, null, 0m));

        // Act
        IReadOnlyList<KpiBreakdownDto>? result = await _sut.GetKpiBreakdownsAsync(security.Id, user.Id, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        var cagrGroup4 = result!.First(b => b.KpiKey == "Cagr").Groups[4];

        cagrGroup4.GroupTotalPercent.Should().Be(0m,
            "when invested capital is 0, CAGR must be 0 to avoid division by zero");

        // CalculateCagr must NOT be called because investedCapital <= 0
        _calcMock.Verify(
            c => c.CalculateCagr(It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<double>()),
            Times.Never,
            "CalculateCagr must be skipped when investedCapital <= 0");
    }

    /// <summary>
    /// The CAGR FormulaText must contain the complete formula as specified.
    /// </summary>
    [Fact]
    public async Task GetKpiBreakdownsAsync_CagrBreakdown_Should_HaveCorrectFormulaText()
    {
        // Arrange
        var (security, user) = SetupSecurityAndUser();
        var buy = CreateBuyPosting(security.Id, DateTime.Today.AddDays(-400), -1_000m, 10m);
        _db.Postings.Add(buy);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        IReadOnlyList<KpiBreakdownDto>? result = await _sut.GetKpiBreakdownsAsync(security.Id, user.Id, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        var cagr = result!.First(b => b.KpiKey == "Cagr");

        cagr.FormulaText.Should().Contain("Marktwert", "formula must reference Marktwert");
        cagr.FormulaText.Should().Contain("Nettodividenden", "formula must reference Nettodividenden");
        cagr.FormulaText.Should().Contain("Investiertes Kapital", "formula must reference Investiertes Kapital");
        cagr.FormulaText.Should().Contain("Jahre", "formula must reference Jahre");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // IRR Breakdown — GetKpiBreakdownsAsync
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The IRR breakdown must contain exactly 2 groups:
    /// [0] "Cashflows (Eingabe für IRR-Berechnung)" and [1] "IRR (Ergebnis)".
    /// </summary>
    [Fact]
    public async Task GetKpiBreakdownsAsync_IrrBreakdown_Should_HaveExactlyTwoGroups()
    {
        // Arrange
        var (security, user) = SetupSecurityAndUser();
        var buy = CreateBuyPosting(security.Id, DateTime.Today.AddDays(-400), -1_000m, 10m);
        _db.Postings.Add(buy);
        _db.SecurityPrices.Add(new SecurityPrice(security.Id, DateTime.Today, 120m));
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        IReadOnlyList<KpiBreakdownDto>? result = await _sut.GetKpiBreakdownsAsync(security.Id, user.Id, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        var irr = result!.First(b => b.KpiKey == "Irr");

        irr.Groups.Should().HaveCount(3, "IRR breakdown must have exactly 3 formula groups");
        irr.Groups[0].GroupName.Should().Be("Cashflows & Barwerte (XIRR-Berechnung)");
        irr.Groups[1].GroupName.Should().Be("Summe der Barwerte (Probe)");
        irr.Groups[2].GroupName.Should().Be("IRR (Ergebnis)");
    }

    /// <summary>
    /// The cashflow timeline items must be sorted chronologically (oldest first),
    /// with the terminal market-value entry last (today's date).
    /// </summary>
    [Fact]
    public async Task GetKpiBreakdownsAsync_IrrBreakdown_CashflowItems_ShouldBeChronologicallySorted()
    {
        // Arrange – buy on day -400, dividend on day -200, another buy on day -100
        var (security, user) = SetupSecurityAndUser();
        var buy1 = CreateBuyPosting(security.Id, DateTime.Today.AddDays(-400), -1_000m, 10m);
        var dividend = new Posting(
            Guid.NewGuid(), PostingKind.Security, null, null, null,
            security.Id, DateTime.Today.AddDays(-200), 50m,
            null, null, null, SecurityPostingSubType.Dividend, null);
        var buy2 = CreateBuyPosting(security.Id, DateTime.Today.AddDays(-100), -500m, 5m);
        _db.Postings.AddRange(buy1, dividend, buy2);
        _db.SecurityPrices.Add(new SecurityPrice(security.Id, DateTime.Today, 110m));
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        IReadOnlyList<KpiBreakdownDto>? result = await _sut.GetKpiBreakdownsAsync(security.Id, user.Id, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        var timelineGroup = result!.First(b => b.KpiKey == "Irr").Groups[0];

        var dates = timelineGroup.Items.Select(i => i.Date).ToList();
        dates.Should().BeInAscendingOrder("cashflow items must be sorted chronologically, oldest first");

        // Last item must be terminal (today)
        timelineGroup.Items[^1].Date.Date.Should().Be(DateTime.Today,
            "the terminal market-value cashflow must have today's date and be last");
    }

    /// <summary>
    /// Each buy posting must appear as a negative cashflow item (Mittelabfluss) in the timeline.
    /// </summary>
    [Fact]
    public async Task GetKpiBreakdownsAsync_IrrBreakdown_BuysMustAppearAsNegativeAmounts()
    {
        // Arrange – single buy
        var (security, user) = SetupSecurityAndUser();
        var buy = CreateBuyPosting(security.Id, DateTime.Today.AddDays(-400), -1_000m, 10m);
        _db.Postings.Add(buy);
        _db.SecurityPrices.Add(new SecurityPrice(security.Id, DateTime.Today, 120m));
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        IReadOnlyList<KpiBreakdownDto>? result = await _sut.GetKpiBreakdownsAsync(security.Id, user.Id, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        var timelineItems = result!.First(b => b.KpiKey == "Irr").Groups[0].Items;

        var buyItems = timelineItems.Where(i => i.Note != null && i.Note.StartsWith("Kauf")).ToList();
        buyItems.Should().HaveCount(1, "one buy posting should produce one cashflow item");
        buyItems[0].Amount.Should().BeNegative("buys are outflows and must be negative cashflows");
        buyItems[0].Amount.Should().Be(-1_000m, "buy amount = −1000 (no linked fees)");
    }

    /// <summary>
    /// Each dividend must appear as a net-positive cashflow (gross minus linked taxes) in the timeline.
    /// </summary>
    [Fact]
    public async Task GetKpiBreakdownsAsync_IrrBreakdown_DividendsMustAppearAsNetPositiveAmounts()
    {
        // Arrange – buy + dividend (80 gross) with a linked tax (−20)
        var (security, user) = SetupSecurityAndUser();
        var divGroupId = Guid.NewGuid();
        var buy = CreateBuyPosting(security.Id, DateTime.Today.AddDays(-400), -1_000m, 10m);
        var dividend = new Posting(
            Guid.NewGuid(), PostingKind.Security, null, null, null,
            security.Id, DateTime.Today.AddDays(-100), 80m,
            null, null, null, SecurityPostingSubType.Dividend, null);
        dividend.SetGroup(divGroupId);
        var tax = new Posting(
            Guid.NewGuid(), PostingKind.Security, null, null, null,
            security.Id, DateTime.Today.AddDays(-100), -20m,
            null, null, null, SecurityPostingSubType.Tax, null);
        tax.SetGroup(divGroupId);
        _db.Postings.AddRange(buy, dividend, tax);
        _db.SecurityPrices.Add(new SecurityPrice(security.Id, DateTime.Today, 110m));
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        IReadOnlyList<KpiBreakdownDto>? result = await _sut.GetKpiBreakdownsAsync(security.Id, user.Id, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        var timelineItems = result!.First(b => b.KpiKey == "Irr").Groups[0].Items;

        var divItems = timelineItems.Where(i => i.Note == "Dividende (netto)").ToList();
        divItems.Should().HaveCount(1, "one dividend posting must produce one net-dividend cashflow item");
        divItems[0].Amount.Should().Be(60m, "net dividend = 80 gross − 20 tax = 60");
        divItems[0].Amount.Should().BePositive("dividends (net) are inflows and must be positive");
    }

    /// <summary>
    /// The last item in the cashflow timeline must be the current market value with today's date.
    /// </summary>
    [Fact]
    public async Task GetKpiBreakdownsAsync_IrrBreakdown_LastItemMustBeCurrentMarketValue()
    {
        // Arrange
        var (security, user) = SetupSecurityAndUser();
        var buy = CreateBuyPosting(security.Id, DateTime.Today.AddDays(-400), -1_000m, 10m);
        _db.Postings.Add(buy);
        // FIFO: 10 shares held; price 130 → market value = 1 300
        _db.SecurityPrices.Add(new SecurityPrice(security.Id, DateTime.Today, 130m));
        _fifoMock
            .Setup(f => f.Calculate(It.IsAny<IReadOnlyList<SecurityTransaction>>()))
            .Returns(new FifoCostBasisResult(1_000m, 0m, Array.Empty<FifoLot>(), 10m, false, null, 0m));
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        IReadOnlyList<KpiBreakdownDto>? result = await _sut.GetKpiBreakdownsAsync(security.Id, user.Id, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        var timelineItems = result!.First(b => b.KpiKey == "Irr").Groups[0].Items;

        var lastItem = timelineItems[^1];
        lastItem.Date.Date.Should().Be(DateTime.Today, "terminal cashflow must have today's date");
        lastItem.Amount.Should().Be(1_300m, "terminal cashflow = 10 shares × 130 = 1 300");
        lastItem.Note.Should().Contain("Marktwert", "note must identify the entry as the market-value terminal cashflow");
    }

    /// <summary>
    /// The IRR result group (Group[1]) must carry the computed IRR as GroupTotalPercent.
    /// </summary>
    [Fact]
    public async Task GetKpiBreakdownsAsync_IrrBreakdown_ResultGroup_ShouldHaveGroupTotalPercent()
    {
        // Arrange – configure the IRR mock to return 12.4 %
        _calcMock.Setup(c => c.CalculateIrr(It.IsAny<IReadOnlyList<CashflowPoint>>()))
            .Returns(0.124m);

        var (security, user) = SetupSecurityAndUser();
        var buy = CreateBuyPosting(security.Id, DateTime.Today.AddDays(-400), -1_000m, 10m);
        _db.Postings.Add(buy);
        _db.SecurityPrices.Add(new SecurityPrice(security.Id, DateTime.Today, 120m));
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        IReadOnlyList<KpiBreakdownDto>? result = await _sut.GetKpiBreakdownsAsync(security.Id, user.Id, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        var irrResultGroup = result!.First(b => b.KpiKey == "Irr").Groups[2];

        irrResultGroup.Items.Should().BeEmpty("result group has no line items, only a percentage total");
        irrResultGroup.GroupTotalPercent.Should().Be(0.124m, "GroupTotalPercent must match the value returned by CalculateIrr");
    }

    /// <summary>
    /// The IRR FormulaText must contain "IRR" and the summation symbol "Σ" or the key term "Cashflow".
    /// </summary>
    [Fact]
    public async Task GetKpiBreakdownsAsync_IrrBreakdown_FormulaText_ShouldContainKeyTerms()
    {
        // Arrange
        var (security, user) = SetupSecurityAndUser();
        var buy = CreateBuyPosting(security.Id, DateTime.Today.AddDays(-400), -1_000m, 10m);
        _db.Postings.Add(buy);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        IReadOnlyList<KpiBreakdownDto>? result = await _sut.GetKpiBreakdownsAsync(security.Id, user.Id, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        var irr = result!.First(b => b.KpiKey == "Irr");

        irr.FormulaText.Should().Contain("IRR", "formula must reference the IRR abbreviation");
        irr.FormulaText.Should().Contain("Σ", "formula must include the summation symbol");
        irr.FormulaText.Should().Contain("Cashflow", "formula must reference Cashflow");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // IRR Breakdown – Discounting (XIRR detail fields)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// When IRR is computable the GroupNote of the timeline group must contain the
    /// formatted IRR rate so the user can read "r = X,XX %".
    /// </summary>
    [Fact]
    public async Task GetKpiBreakdownsAsync_IrrBreakdown_TimelineGroupNote_ShouldContainIrrRate()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        var originalDefaultCulture = CultureInfo.DefaultThreadCurrentCulture;
        var originalDefaultUiCulture = CultureInfo.DefaultThreadCurrentUICulture;

        try
        {
            var culture = CultureInfo.GetCultureInfo("de-DE");
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
            CultureInfo.DefaultThreadCurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;

            // Arrange – IRR mock returns 3.95 %
            _calcMock.Setup(c => c.CalculateIrr(It.IsAny<IReadOnlyList<CashflowPoint>>()))
                .Returns(0.0395m);

            var (security, user) = SetupSecurityAndUser();
            var buy = CreateBuyPosting(security.Id, DateTime.Today.AddDays(-400), -1_000m, 10m);
            _db.Postings.Add(buy);
            _db.SecurityPrices.Add(new SecurityPrice(security.Id, DateTime.Today, 120m));
            await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

            // Act
            IReadOnlyList<KpiBreakdownDto>? result = await _sut.GetKpiBreakdownsAsync(security.Id, user.Id, CancellationToken.None);

            // Assert
            result.Should().NotBeNull();
            var timelineGroup = result!.First(b => b.KpiKey == "Irr").Groups[0];

            timelineGroup.GroupNote.Should().NotBeNullOrEmpty("timeline group must carry a GroupNote when IRR is available");
            timelineGroup.GroupNote.Should().Contain("3,95", "GroupNote must include the IRR rate formatted as a percentage");
            timelineGroup.GroupNote.Should().Contain("%", "GroupNote must include the % sign");
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
            CultureInfo.DefaultThreadCurrentCulture = originalDefaultCulture;
            CultureInfo.DefaultThreadCurrentUICulture = originalDefaultUiCulture;
        }
    }

    /// <summary>
    /// When IRR is computable each timeline item must have YearsSinceT0, DiscountFactor,
    /// and OriginalCashflow populated.
    /// </summary>
    [Fact]
    public async Task GetKpiBreakdownsAsync_IrrBreakdown_ItemsWithIrr_ShouldHaveDiscountFields()
    {
        // Arrange – IRR mock returns a non-null value so discounting is applied
        _calcMock.Setup(c => c.CalculateIrr(It.IsAny<IReadOnlyList<CashflowPoint>>()))
            .Returns(0.05m);

        var (security, user) = SetupSecurityAndUser();
        var buy = CreateBuyPosting(security.Id, DateTime.Today.AddDays(-365), -1_000m, 10m);
        _db.Postings.Add(buy);
        _db.SecurityPrices.Add(new SecurityPrice(security.Id, DateTime.Today, 110m));
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        IReadOnlyList<KpiBreakdownDto>? result = await _sut.GetKpiBreakdownsAsync(security.Id, user.Id, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        var items = result!.First(b => b.KpiKey == "Irr").Groups[0].Items;

        items.Should().NotBeEmpty();
        foreach (var item in items)
        {
            item.YearsSinceT0.Should().NotBeNull("every item must have YearsSinceT0 when IRR is available");
            item.DiscountFactor.Should().NotBeNull("every item must have DiscountFactor when IRR is available");
            item.OriginalCashflow.Should().NotBeNull("every item must have OriginalCashflow when IRR is available");
        }
    }

    /// <summary>
    /// The first cashflow item is at t₀, so its discount factor must equal 1.0 exactly.
    /// </summary>
    [Fact]
    public async Task GetKpiBreakdownsAsync_IrrBreakdown_FirstItem_DiscountFactorShouldBeOne()
    {
        // Arrange
        _calcMock.Setup(c => c.CalculateIrr(It.IsAny<IReadOnlyList<CashflowPoint>>()))
            .Returns(0.08m);

        var (security, user) = SetupSecurityAndUser();
        var buy = CreateBuyPosting(security.Id, DateTime.Today.AddDays(-400), -1_000m, 10m);
        _db.Postings.Add(buy);
        _db.SecurityPrices.Add(new SecurityPrice(security.Id, DateTime.Today, 115m));
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        IReadOnlyList<KpiBreakdownDto>? result = await _sut.GetKpiBreakdownsAsync(security.Id, user.Id, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        var firstItem = result!.First(b => b.KpiKey == "Irr").Groups[0].Items[0];

        firstItem.YearsSinceT0.Should().Be(0m, "the first item is at t₀, years since t₀ must be 0");
        firstItem.DiscountFactor.Should().Be(1m, "discount factor at t=0 is always 1/(1+r)^0 = 1");
    }

    /// <summary>
    /// The Amount of each item must approximately equal OriginalCashflow / (1+r)^t.
    /// </summary>
    [Fact]
    public async Task GetKpiBreakdownsAsync_IrrBreakdown_ItemAmount_ShouldEqualDiscountedCashflow()
    {
        // Arrange – use a known IRR so we can verify the arithmetic
        const decimal irrRate = 0.10m;
        _calcMock.Setup(c => c.CalculateIrr(It.IsAny<IReadOnlyList<CashflowPoint>>()))
            .Returns(irrRate);

        var (security, user) = SetupSecurityAndUser();
        // Buy today (t=0) and another buy 365 days ago
        var buyT0 = CreateBuyPosting(security.Id, DateTime.Today.AddDays(-365), -1_000m, 10m);
        _db.Postings.Add(buyT0);
        _db.SecurityPrices.Add(new SecurityPrice(security.Id, DateTime.Today, 120m));
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        IReadOnlyList<KpiBreakdownDto>? result = await _sut.GetKpiBreakdownsAsync(security.Id, user.Id, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        var items = result!.First(b => b.KpiKey == "Irr").Groups[0].Items;

        foreach (var item in items)
        {
            item.OriginalCashflow.Should().NotBeNull();
            item.YearsSinceT0.Should().NotBeNull();
            item.DiscountFactor.Should().NotBeNull();

            decimal expectedPv = item.OriginalCashflow!.Value * item.DiscountFactor!.Value;
            item.Amount.Should().BeApproximately(expectedPv, 0.01m,
                "Amount must equal OriginalCashflow × DiscountFactor = Cashflow / (1+r)^t");
        }
    }

    /// <summary>
    /// Group[1] "Summe der Barwerte (Probe)" must have GroupTotal approximately equal to 0
    /// when a valid IRR is used (by definition of IRR the sum of present values = 0).
    /// </summary>
    [Fact]
    public async Task GetKpiBreakdownsAsync_IrrBreakdown_SumOfPresentValues_ShouldBeApproxZero()
    {
        // Arrange – use the real IRR calculator via a slightly different mock setup:
        // configure a buy and a market value so the IRR solver would return a real rate.
        // Here we rely on the real CalculateIrr via a concrete mock return value that is
        // self-consistent with the cashflows we supply.
        // Buy: −1 000 at t₀.  Terminal: 1 100 after 1 year.  IRR ≈ 10 %.
        const decimal irrRate = 0.10m;
        _calcMock.Setup(c => c.CalculateIrr(It.IsAny<IReadOnlyList<CashflowPoint>>()))
            .Returns(irrRate);
        _fifoMock
            .Setup(f => f.Calculate(It.IsAny<IReadOnlyList<SecurityTransaction>>()))
            .Returns(new FifoCostBasisResult(1_000m, 0m, Array.Empty<FifoLot>(), 10m, false, null, 0m));

        var (security, user) = SetupSecurityAndUser();
        var buy = CreateBuyPosting(security.Id, DateTime.Today.AddDays(-365), -1_000m, 10m);
        _db.Postings.Add(buy);
        // 10 shares × 110 = 1 100 → terminal cashflow
        _db.SecurityPrices.Add(new SecurityPrice(security.Id, DateTime.Today, 110m));
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        IReadOnlyList<KpiBreakdownDto>? result = await _sut.GetKpiBreakdownsAsync(security.Id, user.Id, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        var sumGroup = result!.First(b => b.KpiKey == "Irr").Groups[1];

        sumGroup.GroupName.Should().Be("Summe der Barwerte (Probe)");
        // The sum of PVs = PV(−1000 at t=0) + PV(1100 at t=1)
        // = −1000 + 1100/1.10 = −1000 + 1000 = 0
        sumGroup.GroupTotal.Should().BeApproximately(0m, 1.0m,
            "sum of all present values must be approximately 0 when IRR is correctly computed");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GetPerformanceChartDataAsync
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// After a full sell of all shares, all chart data points for dates on or after the
    /// sell date must have a portfolio value of 0 (no shares held).
    /// <para>
    /// Regression guard for a sign-convention bug: domain sell quantities are stored as
    /// negative values (e.g. −10). When <c>ComputeSharesHeldOnDate</c> subtracts a negative
    /// quantity (<c>shares -= −10</c>), shares are incorrectly increased instead of decreased,
    /// causing the market value to double rather than drop to zero.
    /// </para>
    /// </summary>
    [Fact]
    public async Task GetPerformanceChartDataAsync_Should_ShowZeroMarketValue_AfterFullSell()
    {
        // Arrange
        var (security, user) = SetupSecurityAndUser();

        var buyDate = DateTime.Today.AddDays(-10);
        var sellDate = DateTime.Today.AddDays(-5);

        // Buy: 10 shares; amount is negative (cash outflow), quantity is positive
        var buy = CreateBuyPosting(security.Id, buyDate, -1_000m, 10m);

        // Sell: 10 shares; amount is positive (cash inflow), quantity is NEGATIVE (domain convention)
        var sell = new Posting(
            Guid.NewGuid(),
            PostingKind.Security,
            null, null, null,
            security.Id,
            sellDate,
            1_100m,
            null, null, null,
            SecurityPostingSubType.Sell,
            -10m); // negative quantity — the sign convention that triggers the bug

        _db.Postings.AddRange(buy, sell);

        // Price of 120 on and after the sell date; the test verifies market value = 0, not 2400
        _db.SecurityPrices.Add(new SecurityPrice(security.Id, buyDate, 100m));
        _db.SecurityPrices.Add(new SecurityPrice(security.Id, sellDate, 120m));
        _db.SecurityPrices.Add(new SecurityPrice(security.Id, DateTime.Today, 120m));

        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        PerformanceChartDataDto? result = await _sut.GetPerformanceChartDataAsync(
            security.Id, user.Id, ChartTimeRange.All, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();

        var pointsAfterSell = result!.PortfolioValues
            .Where(p => p.Date.Date >= sellDate.Date)
            .ToList();

        pointsAfterSell.Should().NotBeEmpty("there must be chart data points on and after the sell date");
        pointsAfterSell.Should().AllSatisfy(p =>
            p.Value.Should().Be(0m,
                $"market value on {p.Date:d} must be 0 because all shares have been sold " +
                $"(bug: negative sell quantity subtracted as positive would double share count instead)"));
    }

    /// <summary>
    /// When no real price history exists for a security, ComputePerformanceChartDataAsync must
    /// simulate prices from buy/sell transaction amounts. The implied unit price for each
    /// transaction is |Amount| / |Quantity|. Between two anchor points (e.g., buy price and sell
    /// price), the price is linearly interpolated for each calendar day.
    /// <para>
    /// Example: buy 10 shares for 100€ on 15.01 (price = 10€); sell 10 shares for 150€ on 20.01
    /// (price = 15€). Expected simulated prices: 15.01→10€, 16.01→11€, 17.01→12€, 18.01→13€,
    /// 19.01→14€, 20.01→15€. Portfolio values are shares × price (0 after sell).
    /// </para>
    /// </summary>
    [Fact]
    public async Task GetPerformanceChartDataAsync_Should_SimulatePrices_WhenNoPriceHistoryExists()
    {
        // Arrange
        var (security, user) = SetupSecurityAndUser();

        var buyDate = new DateTime(2020, 1, 15);
        var sellDate = new DateTime(2020, 1, 20);

        // Buy: 10 shares, 100€ outflow → implied price = 10€/share
        var buy = CreateBuyPosting(security.Id, buyDate, -100m, 10m);

        // Sell: 10 shares, 150€ inflow, negative quantity (domain convention)
        // → implied price = |150| / |-10| = 15€/share
        var sell = new Posting(
            Guid.NewGuid(),
            PostingKind.Security,
            null, null, null,
            security.Id,
            sellDate,
            150m,
            null, null, null,
            SecurityPostingSubType.Sell,
            -10m);

        _db.Postings.AddRange(buy, sell);
        // deliberately no SecurityPrices — simulation must produce the chart data
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        PerformanceChartDataDto? result = await _sut.GetPerformanceChartDataAsync(
            security.Id, user.Id, ChartTimeRange.All, CancellationToken.None);

        // Assert
        result.Should().NotBeNull("chart must be produced even without real price data");

        var valuesByDate = result!.PortfolioValues.ToDictionary(p => p.Date.Date);

        // 15.01: 10 shares × 10€ = 100
        valuesByDate[buyDate.Date].Value.Should().BeApproximately(100m, 0.01m,
            "buy date: 10 shares × simulated price 10€");

        // 16.01 – 19.01: linearly interpolated prices 11€ – 14€
        valuesByDate[new DateTime(2020, 1, 16)].Value.Should().BeApproximately(110m, 0.01m,
            "day +1: 10 shares × 11€");
        valuesByDate[new DateTime(2020, 1, 17)].Value.Should().BeApproximately(120m, 0.01m,
            "day +2: 10 shares × 12€");
        valuesByDate[new DateTime(2020, 1, 18)].Value.Should().BeApproximately(130m, 0.01m,
            "day +3: 10 shares × 13€");
        valuesByDate[new DateTime(2020, 1, 19)].Value.Should().BeApproximately(140m, 0.01m,
            "day +4: 10 shares × 14€");

        // 20.01: all shares sold → 0 shares × 15€ = 0
        valuesByDate[sellDate.Date].Value.Should().Be(0m,
            "sell date: all shares sold, market value = 0");
    }

    /// <summary>
    /// When real price history only exists AFTER the last sell (i.e., during the ownership
    /// period there are no prices), the chart must still show simulated prices for the
    /// ownership period. The post-sell data points must show portfolio value 0 because no
    /// shares are held regardless of the real price.
    /// <para>
    /// Scenario: buy Jan 15 (10 shares, 100€ → 10€/share), sell Jan 20 (150€ → 15€/share),
    /// first real price on Jan 25 (20€). The Jan 15-19 period must be simulated.
    /// </para>
    /// </summary>
    [Fact]
    public async Task GetPerformanceChartDataAsync_Should_SimulatePrices_ForOwnershipPeriod_WhenRealPricesStartAfterSell()
    {
        // Arrange
        var (security, user) = SetupSecurityAndUser();

        var buyDate = new DateTime(2020, 1, 15);
        var sellDate = new DateTime(2020, 1, 20);
        var priceDate = new DateTime(2020, 1, 25); // real price only AFTER the sell

        var buy = CreateBuyPosting(security.Id, buyDate, -100m, 10m); // 10€/share
        var sell = new Posting(
            Guid.NewGuid(),
            PostingKind.Security,
            null, null, null,
            security.Id,
            sellDate,
            150m,
            null, null, null,
            SecurityPostingSubType.Sell,
            -10m); // 15€/share

        _db.Postings.AddRange(buy, sell);
        _db.SecurityPrices.Add(new SecurityPrice(security.Id, priceDate, 20m)); // only after sell
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        PerformanceChartDataDto? result = await _sut.GetPerformanceChartDataAsync(
            security.Id, user.Id, ChartTimeRange.All, CancellationToken.None);

        // Assert
        result.Should().NotBeNull("chart must include the ownership period even though real prices only start after the sell");

        var valuesByDate = result!.PortfolioValues.ToDictionary(p => p.Date.Date);

        // Ownership start must be present (simulated)
        valuesByDate.Should().ContainKey(buyDate.Date,
            "buy date must be covered by simulated prices");

        // Jan 15: 10 shares × 10€ = 100€
        valuesByDate[buyDate.Date].Value.Should().BeApproximately(100m, 0.01m,
            "buy date: 10 shares × simulated price 10€");

        // Sell date and after: no shares held → 0
        valuesByDate[sellDate.Date].Value.Should().Be(0m,
            "sell date: all shares sold → 0");
        valuesByDate[priceDate.Date].Value.Should().Be(0m,
            "after sell with real price: 0 shares × 20€ = 0");
    }

    /// <summary>
    /// When real price history starts WITHIN the ownership period (after the buy but before
    /// the sell), the chart must simulate prices for the gap before the first real price and
    /// then use real prices for the remainder. This ensures a continuous chart from the first
    /// buy onwards.
    /// <para>
    /// Scenario: buy Jan 1 (10 shares, 100€ → 10€/share), first real price Jan 11 (25€),
    /// sell Jan 21 (300€ → 30€/share). The Jan 1-10 period must use simulated prices;
    /// Jan 11+ must use the real price (25€, NOT the simulated ~15€).
    /// </para>
    /// </summary>
    [Fact]
    public async Task GetPerformanceChartDataAsync_Should_SimulatePrices_ForGapBeforeFirstRealPrice_WhenRealPricesStartMidOwnership()
    {
        // Arrange
        var (security, user) = SetupSecurityAndUser();

        var buyDate = new DateTime(2020, 1, 1);
        var priceDate = new DateTime(2020, 1, 11); // real price starts mid-ownership
        var sellDate = new DateTime(2020, 1, 21);

        // Buy: 10 shares, 100€ → 10€/share
        var buy = CreateBuyPosting(security.Id, buyDate, -100m, 10m);

        // Sell: 10 shares, 300€ → 30€/share
        // (simulated price on Jan 11 would be ~15€, real is 25€ — clearly different)
        var sell = new Posting(
            Guid.NewGuid(),
            PostingKind.Security,
            null, null, null,
            security.Id,
            sellDate,
            300m,
            null, null, null,
            SecurityPostingSubType.Sell,
            -10m);

        _db.Postings.AddRange(buy, sell);
        _db.SecurityPrices.Add(new SecurityPrice(security.Id, priceDate, 25m)); // mid-period real price
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        PerformanceChartDataDto? result = await _sut.GetPerformanceChartDataAsync(
            security.Id, user.Id, ChartTimeRange.All, CancellationToken.None);

        // Assert
        result.Should().NotBeNull("chart must start from the buy date, not the first real price");

        var valuesByDate = result!.PortfolioValues.ToDictionary(p => p.Date.Date);

        // Jan 1: pre-price-history gap → must be simulated (10€/share)
        valuesByDate.Should().ContainKey(buyDate.Date,
            "buy date must be present via simulated price");
        valuesByDate[buyDate.Date].Value.Should().BeApproximately(100m, 0.01m,
            "buy date: 10 shares × simulated price 10€ = 100€");

        // Jan 11: must use real price 25€, NOT simulated ~15€
        valuesByDate.Should().ContainKey(priceDate.Date,
            "first real price date must be in the chart");
        valuesByDate[priceDate.Date].Value.Should().BeApproximately(250m, 0.01m,
            "Jan 11: 10 shares × real price 25€ = 250€ (not simulated ~150€)");

        // Jan 21: all sold → 0
        valuesByDate[sellDate.Date].Value.Should().Be(0m,
            "sell date: all shares sold → 0");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GetPeriodicReturnsAsync – Modified Dietz
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// When a security was bought in year A and sold in year B (B &lt; current year),
    /// the result must contain entries only for years A through B, not beyond.
    /// A fully closed position must not produce empty years after the sale.
    /// </summary>
    [Fact]
    public async Task GetPeriodicReturnsAsync_Should_StopAtLastTransactionYear_When_PositionFullyClosed()
    {
        // Arrange – buy on 2020-06-01, sell on 2021-03-01 (both in the past)
        var (security, user) = SetupSecurityAndUser();

        var buyDate = new DateTime(2020, 6, 1);
        var sellDate = new DateTime(2021, 3, 1);

        var buy = CreateBuyPosting(security.Id, buyDate, -1_000m, 10m);
        var sell = new Posting(
            Guid.NewGuid(), PostingKind.Security,
            null, null, null, security.Id, sellDate,
            1_200m, null, null, null, SecurityPostingSubType.Sell, -10m);

        _db.Postings.AddRange(buy, sell);
        _db.SecurityPrices.Add(new SecurityPrice(security.Id, buyDate, 100m));
        _db.SecurityPrices.Add(new SecurityPrice(security.Id, sellDate, 120m));
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        PeriodicReturnsDto? result = await _sut.GetPeriodicReturnsAsync(
            security.Id, user.Id, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();

        var years = result!.AnnualReturns.Select(r => r.Year).ToList();
        years.Should().BeEquivalentTo(new[] { 2020, 2021 },
            "only the years with activity should appear; no empty years after the sell");
        result.AnnualReturns.Should().AllSatisfy(r =>
            r.IsYtd.Should().BeFalse("position is fully closed, no year is a YTD estimate"));
    }

    /// <summary>
    /// In the year of the first purchase (starting portfolio value = 0), the Modified Dietz
    /// formula must return a meaningful return (positive when stock appreciated, negative when
    /// it declined) instead of 0 as the old simple-HPR formula produced.
    /// </summary>
    [Fact]
    public async Task GetPeriodicReturnsAsync_Should_ReturnPositiveAnnualReturn_When_StockApprecia­tedInBuyYear()
    {
        // Arrange – buy 10 shares on 2022-01-02 for 1 000 € (100 €/share).
        //           Year-end price is 120 €/share → 20 % gain.
        var (security, user) = SetupSecurityAndUser();

        var buyDate = new DateTime(2022, 1, 2);
        var yearEndDate = new DateTime(2022, 12, 31);

        var buy = CreateBuyPosting(security.Id, buyDate, -1_000m, 10m);

        _db.Postings.Add(buy);
        _db.SecurityPrices.Add(new SecurityPrice(security.Id, buyDate, 100m));
        _db.SecurityPrices.Add(new SecurityPrice(security.Id, yearEndDate, 120m));
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        PeriodicReturnsDto? result = await _sut.GetPeriodicReturnsAsync(
            security.Id, user.Id, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();

        var annual2022 = result!.AnnualReturns.FirstOrDefault(r => r.Year == 2022);
        annual2022.Should().NotBeNull("2022 must have an annual return entry");
        annual2022!.ReturnPercent.Should().BeGreaterThan(0m,
            "the stock appreciated 20 % in the buy year, so the return must be positive");
    }

    /// <summary>
    /// In the year of a full sale (ending portfolio value = 0), the Modified Dietz formula must
    /// produce a positive return when the sell proceeds exceed the start-of-year market value
    /// minus fees.
    /// </summary>
    [Fact]
    public async Task GetPeriodicReturnsAsync_Should_ReturnPositiveAnnualReturn_When_SoldWithGainInSellYear()
    {
        // Arrange – buy 10 shares in 2022, sell them in 2023 for a higher price.
        //   2022-01-02: buy 10 shares @ 100 €  → amount -1 000 €
        //   2022-12-31: price = 100 € (flat year)
        //   2023-06-01: sell 10 shares @ 130 € → amount +1 300 €
        //   2023-06-01: fee                    → amount -   10 €
        var (security, user) = SetupSecurityAndUser();

        var buyDate = new DateTime(2022, 1, 2);
        var sellDate = new DateTime(2023, 6, 1);

        var buy = CreateBuyPosting(security.Id, buyDate, -1_000m, 10m);

        var sell = new Posting(
            Guid.NewGuid(), PostingKind.Security,
            null, null, null, security.Id, sellDate,
            1_300m, null, null, null, SecurityPostingSubType.Sell, -10m);

        var fee = new Posting(
            Guid.NewGuid(), PostingKind.Security,
            null, null, null, security.Id, sellDate,
            -10m, null, null, null, SecurityPostingSubType.Fee, null);

        _db.Postings.AddRange(buy, sell, fee);
        _db.SecurityPrices.Add(new SecurityPrice(security.Id, buyDate, 100m));
        _db.SecurityPrices.Add(new SecurityPrice(security.Id, new DateTime(2022, 12, 31), 100m));
        _db.SecurityPrices.Add(new SecurityPrice(security.Id, sellDate, 130m));
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        PeriodicReturnsDto? result = await _sut.GetPeriodicReturnsAsync(
            security.Id, user.Id, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();

        var annual2023 = result!.AnnualReturns.FirstOrDefault(r => r.Year == 2023);
        annual2023.Should().NotBeNull("2023 must have an annual return entry");
        annual2023!.ReturnPercent.Should().BeGreaterThan(0m,
            "the position was sold at a 30 % gain in 2023, so the annual return must be positive");
    }

    /// <summary>
    /// Monthly returns must not be null for months where the position was held, even if the
    /// position was opened with a buy in that same month (starting value = 0).
    /// </summary>
    [Fact]
    public async Task GetPeriodicReturnsAsync_Should_ProduceMonthlyReturn_When_PositionOpenedMidMonth()
    {
        // Arrange – buy 10 shares on 2022-03-15 for 1 000 € (100 €/share).
        //           Month-end price (March 31) is 110 €/share.
        var (security, user) = SetupSecurityAndUser();

        var buyDate = new DateTime(2022, 3, 15);
        var marchEndDate = new DateTime(2022, 3, 31);

        var buy = CreateBuyPosting(security.Id, buyDate, -1_000m, 10m);

        _db.Postings.Add(buy);
        _db.SecurityPrices.Add(new SecurityPrice(security.Id, buyDate, 100m));
        _db.SecurityPrices.Add(new SecurityPrice(security.Id, marchEndDate, 110m));
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        PeriodicReturnsDto? result = await _sut.GetPeriodicReturnsAsync(
            security.Id, user.Id, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();

        var march2022 = result!.MonthlyReturns.FirstOrDefault(r => r.Year == 2022 && r.Month == 3);
        march2022.Should().NotBeNull("March 2022 must be present");
        march2022!.ReturnPercent.Should().NotBeNull(
            "the month has capital deployed (buy + prices), so a return must be computed");
        march2022.ReturnPercent!.Value.Should().BeGreaterThan(0m,
            "stock gained 10 % from the buy day to month-end");
    }
}
