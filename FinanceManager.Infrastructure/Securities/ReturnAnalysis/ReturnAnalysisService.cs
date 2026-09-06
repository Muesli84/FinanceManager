using FinanceManager.Application.Securities.ReturnAnalysis;
using FinanceManager.Domain.Postings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FinanceManager.Infrastructure.Securities.ReturnAnalysis;

/// <summary>
/// Orchestrates return analysis for a single security. Implements <see cref="IReturnAnalysisService"/>.
/// All methods are user-scoped and cache results for <see cref="CacheTtl"/> (1 hour) by default.
/// Database ownership is enforced by joining postings and price data through the Security entity (Blocker S-1).
/// </summary>
public sealed class ReturnAnalysisService : IReturnAnalysisService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(1);

    private readonly AppDbContext _db;
    private readonly IReturnCalculationService _calc;
    private readonly IFifoCostBasisCalculator _fifo;
    private readonly IReturnAnalysisCache _cache;
    private readonly ILogger<ReturnAnalysisService> _logger;
    private readonly IReturnAnalysisLocalizer _localizer;

    /// <summary>
    /// Initializes a new instance of <see cref="ReturnAnalysisService"/>.
    /// </summary>
    /// <param name="db">Application database context.</param>
    /// <param name="calc">Pure financial calculations service.</param>
    /// <param name="fifo">FIFO cost basis calculator.</param>
    /// <param name="cache">Return analysis cache abstraction.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="localizer">Localizer for KPI labels, formula texts and item notes.</param>
    public ReturnAnalysisService(
        AppDbContext db,
        IReturnCalculationService calc,
        IFifoCostBasisCalculator fifo,
        IReturnAnalysisCache cache,
        ILogger<ReturnAnalysisService> logger,
        IReturnAnalysisLocalizer localizer)
    {
        _db = db;
        _calc = calc;
        _fifo = fifo;
        _cache = cache;
        _logger = logger;
        _localizer = localizer;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // IReturnAnalysisService
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public Task<ReturnSummaryDto?> GetReturnSummaryAsync(Guid securityId, Guid ownerUserId, CancellationToken ct)
    {
        var key = ReturnAnalysisCacheKeys.Summary(securityId, ownerUserId);
        return _cache.GetOrCreateAsync(key, () => ComputeReturnSummaryAsync(securityId, ownerUserId, ct), CacheTtl);
    }

    /// <inheritdoc/>
    public Task<SparklineDataDto?> GetSparklineDataAsync(Guid securityId, Guid ownerUserId, CancellationToken ct)
    {
        var key = ReturnAnalysisCacheKeys.Sparkline(securityId, ownerUserId);
        return _cache.GetOrCreateAsync(key, () => ComputeSparklineDataAsync(securityId, ownerUserId, ct), CacheTtl);
    }

    /// <inheritdoc/>
    public Task<DetailedReturnMetricsDto?> GetDetailedMetricsAsync(Guid securityId, Guid ownerUserId, CancellationToken ct)
    {
        var key = ReturnAnalysisCacheKeys.Metrics(securityId, ownerUserId);
        return _cache.GetOrCreateAsync(key, () => ComputeDetailedMetricsAsync(securityId, ownerUserId, ct), CacheTtl);
    }

    /// <inheritdoc/>
    public Task<PeriodicReturnsDto?> GetPeriodicReturnsAsync(Guid securityId, Guid ownerUserId, CancellationToken ct)
    {
        var key = ReturnAnalysisCacheKeys.Periodic(securityId, ownerUserId);
        return _cache.GetOrCreateAsync(key, () => ComputePeriodicReturnsAsync(securityId, ownerUserId, ct), CacheTtl);
    }

    /// <inheritdoc/>
    public Task<CashflowTimelineDto?> GetCashflowTimelineAsync(Guid securityId, Guid ownerUserId, CancellationToken ct)
    {
        var key = ReturnAnalysisCacheKeys.Cashflow(securityId, ownerUserId);
        return _cache.GetOrCreateAsync(key, () => ComputeCashflowTimelineAsync(securityId, ownerUserId, ct), CacheTtl);
    }

    /// <inheritdoc/>
    public Task<PerformanceChartDataDto?> GetPerformanceChartDataAsync(Guid securityId, Guid ownerUserId, ChartTimeRange timeRange, CancellationToken ct)
    {
        var key = ReturnAnalysisCacheKeys.Chart(securityId, ownerUserId, timeRange.ToString());
        return _cache.GetOrCreateAsync(key, () => ComputePerformanceChartDataAsync(securityId, ownerUserId, timeRange, ct), CacheTtl);
    }

    /// <inheritdoc/>
    public Task<BenchmarkComparisonDto?> GetBenchmarkComparisonAsync(Guid securityId, Guid ownerUserId, CancellationToken ct)
    {
        var key = ReturnAnalysisCacheKeys.Benchmark(securityId, ownerUserId);
        return _cache.GetOrCreateAsync(key, () => ComputeBenchmarkComparisonAsync(securityId, ownerUserId, ct), CacheTtl);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<KpiBreakdownDto>?> GetKpiBreakdownsAsync(Guid securityId, Guid ownerUserId, CancellationToken ct)
    {
        var key = ReturnAnalysisCacheKeys.KpiBreakdown(securityId, ownerUserId);
        return _cache.GetOrCreateAsync(key, () => ComputeKpiBreakdownsAsync(securityId, ownerUserId, ct), CacheTtl);
    }

    /// <inheritdoc/>
    public async Task<ReturnAnalysisSettingsDto?> GetUserSettingsAsync(Guid ownerUserId, CancellationToken ct)
    {
        var user = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == ownerUserId, ct);

        if (user == null) return null;

        string? benchmarkName = null;
        if (user.BenchmarkSecurityId.HasValue)
        {
            benchmarkName = await _db.Securities
                .AsNoTracking()
                .Where(s => s.Id == user.BenchmarkSecurityId.Value && s.OwnerUserId == ownerUserId)
                .Select(s => s.Name)
                .FirstOrDefaultAsync(ct);
        }

        return new ReturnAnalysisSettingsDto(
            user.BenchmarkSecurityId,
            benchmarkName,
            user.ShowSharpeRatio,
            user.RiskFreeRate);
    }

    /// <inheritdoc/>
    public async Task UpdateUserSettingsAsync(Guid ownerUserId, Guid? benchmarkSecurityId, bool showSharpeRatio, decimal riskFreeRate, CancellationToken ct)
    {
        // S-3: verify benchmark security ownership
        if (benchmarkSecurityId.HasValue)
        {
            bool benchmarkOwned = await _db.Securities
                .AnyAsync(s => s.Id == benchmarkSecurityId.Value && s.OwnerUserId == ownerUserId, ct);

            if (!benchmarkOwned)
            {
                _logger.LogWarning(
                    "UpdateUserSettingsAsync: Benchmark security {BenchmarkId} not found or not owned by user {UserId}.",
                    benchmarkSecurityId.Value, ownerUserId);
                throw new ArgumentException("Benchmark security not found or not owned by this user.", nameof(benchmarkSecurityId));
            }
        }

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == ownerUserId, ct);
        if (user == null)
        {
            _logger.LogWarning("UpdateUserSettingsAsync: User {UserId} not found.", ownerUserId);
            return;
        }

        user.SetReturnAnalysisSettings(benchmarkSecurityId, showSharpeRatio, riskFreeRate);
        await _db.SaveChangesAsync(ct);
    }

    /// <inheritdoc/>
    public Task InvalidateCacheAsync(Guid securityId, Guid ownerUserId)
    {
        var token = ReturnAnalysisCacheKeys.SecurityUserToken(securityId, ownerUserId);
        return _cache.InvalidateAsync(token);
    }

    /// <inheritdoc/>
    public Task InvalidateAllUserCachesAsync(Guid ownerUserId)
        => _cache.InvalidateAsync(ownerUserId.ToString());

    // ─────────────────────────────────────────────────────────────────────────
    // Private computation methods
    // ─────────────────────────────────────────────────────────────────────────

    private async Task<ReturnSummaryDto?> ComputeReturnSummaryAsync(
        Guid securityId, Guid ownerUserId, CancellationToken ct)
    {
        // Verify security exists and is owned by user
        var security = await _db.Securities
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == securityId && s.OwnerUserId == ownerUserId, ct);

        if (security == null) return null;

        var transactions = await LoadPostingsAsync(securityId, ownerUserId, ct);
        if (transactions.Count == 0)
        {
            _logger.LogDebug("ComputeReturnSummaryAsync: No transactions for security {SecurityId}.", securityId);
            return null;
        }

        var fifoResult = _fifo.Calculate(transactions);

        // Latest price
        var latestPrice = await _db.SecurityPrices
            .AsNoTracking()
            .Where(sp => sp.SecurityId == securityId)
            .OrderByDescending(sp => sp.Date)
            .Select(sp => new { sp.Date, sp.Close })
            .FirstOrDefaultAsync(ct);

        decimal currentPricePerShare = latestPrice?.Close ?? 0m;
        decimal sharesHeld = fifoResult.TotalSharesHeld;
        decimal currentMarketValue = sharesHeld * currentPricePerShare;

        // Net dividends= sum of Dividend postings - sum of Tax postings (taxes reduce dividend income)
        decimal grossDividends = transactions
            .Where(t => t.Type == SecurityPostingSubType.Dividend)
            .Sum(t => t.Amount);
        decimal taxes = transactions
            .Where(t => t.Type == SecurityPostingSubType.Tax)
            .Sum(t => Math.Abs(t.Amount));
        decimal netDividends = grossDividends - taxes;

        // Sales proceeds net of fees:
        // - Get all Sell transactions
        // - For each sell with a GroupId, sum its linked fees and subtract from the sale amount
        var sells = transactions.Where(t => t.Type == SecurityPostingSubType.Sell).ToList();
        var fees = transactions.Where(t => t.Type == SecurityPostingSubType.Fee).ToList();
        var feesByGroupId = fees.ToLookup(f => f.GroupId);

        decimal totalSalesProceeds = 0m;
        foreach (var sell in sells)
        {
            decimal saleAmount = sell.Amount;
            decimal linkedFees = sell.GroupId != Guid.Empty
                ? feesByGroupId[sell.GroupId].Sum(f => Math.Abs(f.Amount)) // fees are negative in domain, make positive
                : 0m;
            totalSalesProceeds += (saleAmount - linkedFees);
        }

        // Two distinct invested-capital figures are needed:
        //
        // 1. historicalInvestedCapital – the total cash outflow for acquiring shares (ever).
        //    Used as the denominator for TotalReturn%, CAGR and absolute return.
        //    = sum(abs(Buy amounts)) + sum(abs(Fee amounts) where fee is NOT linked to a sell)
        //    Sell-linked fees are excluded because they are already deducted from salesProceeds.
        //
        // 2. investedCapital (for display as "Investiert") – the FIFO cost basis of the
        //    CURRENTLY HELD shares. Zero when all shares have been sold.
        //    = fifoResult.TotalCostBasis
        var sellGroupIds = sells
            .Where(s => s.GroupId != Guid.Empty)
            .Select(s => s.GroupId)
            .ToHashSet();

        decimal historicalInvestedCapital =
            transactions.Where(t => t.Type == SecurityPostingSubType.Buy)
                        .Sum(t => Math.Abs(t.Amount))
            + transactions.Where(t => t.Type == SecurityPostingSubType.Fee
                                       && !sellGroupIds.Contains(t.GroupId))
                          .Sum(t => Math.Abs(t.Amount));

        // FIFO cost basis of currently held shares only (no standalone fees), zero when fully sold.
        // = sum of remaining lot TotalCosts (buy price + linked fees per lot, pro-rated by FIFO consumption)
        // StandaloneFeeTotal is intentionally excluded here: it cannot be attributed to specific remaining
        // shares and would misrepresent "Investiert" when a position is fully sold (all standalone fees
        // remain in TotalCostBasis even though 0 shares are held).
        decimal investedCapital = fifoResult.TotalCostBasis - fifoResult.StandaloneFeeTotal;

        decimal? totalReturnPercent = _calc.CalculateTotalReturn(historicalInvestedCapital, currentMarketValue + totalSalesProceeds, netDividends);
        decimal totalReturnAbsolute = currentMarketValue + totalSalesProceeds + netDividends - historicalInvestedCapital;

        // CAGR: from first buy date to last sell date (if fully sold) or today
        var firstBuy = transactions
            .Where(t => t.Type == SecurityPostingSubType.Buy)
            .OrderBy(t => t.Date)
            .FirstOrDefault();

        // When position is fully sold, investment period ends at last sell date — not today.
        DateTime cagrEndDate = sharesHeld <= 0m && sells.Count > 0
            ? sells.Max(t => t.Date)
            : DateTime.Today;

        decimal? cagr = null;
        if (firstBuy != null && historicalInvestedCapital > 0m)
        {
            double years = (cagrEndDate.Date - firstBuy.Date.Date).TotalDays / 365.25;
            if (years >= 1.0)
            {
                cagr = _calc.CalculateCagr(historicalInvestedCapital, currentMarketValue + totalSalesProceeds + netDividends, years);
            }
        }

        // IRR cashflows: buys are negative outflows, sells + dividends - taxes are positive inflows
        // Current market value is the terminal inflow
        var irrCashflows = BuildIrrCashflows(transactions, currentMarketValue, DateTime.Today);
        decimal? irr = _calc.CalculateIrr(irrCashflows);

        decimal costBasisPerShare = sharesHeld > 0m
            ? investedCapital / sharesHeld
            : 0m;

        bool hasMissingPrices = latestPrice == null
            || (fifoResult.HasOversellWarning);

        string? missingPricesHint = latestPrice == null
            ? _localizer["Warning_NoPriceAvailable"]
            : fifoResult.HasOversellWarning
                ? fifoResult.OversellWarningMessage
                : null;

        return new ReturnSummaryDto(
            InvestedCapital: investedCapital,
            CurrentMarketValue: currentMarketValue,
            TotalReturnAbsolute: totalReturnAbsolute,
            TotalReturnPercent: totalReturnPercent ?? 0m,
            Cagr: cagr,
            Irr: irr,
            CostBasisPerShare: costBasisPerShare,
            CurrentPricePerShare: currentPricePerShare,
            TotalSharesHeld: sharesHeld,
            NetDividends: netDividends,
            CurrencyCode: security.CurrencyCode,
            HasMissingPrices: hasMissingPrices,
            MissingPricesHint: missingPricesHint);
    }

    private async Task<SparklineDataDto?> ComputeSparklineDataAsync(
        Guid securityId, Guid ownerUserId, CancellationToken ct)
    {
        var transactions = await LoadPostingsAsync(securityId, ownerUserId, ct);
        if (transactions.Count == 0) return null;

        var firstDate = transactions.Min(t => t.Date).Date;
        var prices = await LoadPriceHistoryAsync(securityId, ownerUserId, firstDate, DateTime.Today, ct);

        if (prices.Count < 30) return null;

        var filledPrices = ForwardFill(prices, firstDate, DateTime.Today);

        var points = BuildSparklinePoints(transactions, filledPrices);
        return new SparklineDataDto(points);
    }

    private async Task<DetailedReturnMetricsDto?> ComputeDetailedMetricsAsync(
        Guid securityId, Guid ownerUserId, CancellationToken ct)
    {
        var security = await _db.Securities
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == securityId && s.OwnerUserId == ownerUserId, ct);

        if (security == null) return null;

        var user = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == ownerUserId, ct);

        if (user == null) return null;

        var transactions = await LoadPostingsAsync(securityId, ownerUserId, ct);
        if (transactions.Count == 0) return null;

        var fifoResult = _fifo.Calculate(transactions);

        var firstDate = transactions.Min(t => t.Date).Date;
        var prices = await LoadPriceHistoryAsync(securityId, ownerUserId, firstDate, DateTime.Today, ct);
        var filledPrices = ForwardFill(prices, firstDate, DateTime.Today);

        var latestPrice = filledPrices.Count > 0 ? filledPrices[^1].Close : 0m;
        decimal sharesHeld = fifoResult.TotalSharesHeld;
        decimal currentMarketValue = sharesHeld * latestPrice;

        decimal grossDividends = transactions
            .Where(t => t.Type == SecurityPostingSubType.Dividend)
            .Sum(t => t.Amount);
        decimal totalTaxes = transactions
            .Where(t => t.Type == SecurityPostingSubType.Tax)
            .Sum(t => Math.Abs(t.Amount));
        decimal totalFees = transactions
            .Where(t => t.Type == SecurityPostingSubType.Fee)
            .Sum(t => Math.Abs(t.Amount));
        decimal netDividends = grossDividends - totalTaxes;

        // Sales proceeds net of fees:
        // - Get all Sell transactions
        // - For each sell with a GroupId, sum its linked fees and subtract from the sale amount
        var sells = transactions.Where(t => t.Type == SecurityPostingSubType.Sell).ToList();
        var fees = transactions.Where(t => t.Type == SecurityPostingSubType.Fee).ToList();
        var feesByGroupId = fees.ToLookup(f => f.GroupId);

        decimal totalSalesProceeds = 0m;
        foreach (var sell in sells)
        {
            decimal saleAmount = sell.Amount;
            decimal linkedFees = sell.GroupId != Guid.Empty
                ? feesByGroupId[sell.GroupId].Sum(f => Math.Abs(f.Amount)) // fees are negative in domain, make positive
                : 0m;
            totalSalesProceeds += (saleAmount - linkedFees);
        }

        // See ComputeReturnSummaryAsync for the two-capital explanation.
        var sellGroupIds = sells
            .Where(s => s.GroupId != Guid.Empty)
            .Select(s => s.GroupId)
            .ToHashSet();

        decimal historicalInvestedCapital =
            transactions.Where(t => t.Type == SecurityPostingSubType.Buy)
                        .Sum(t => Math.Abs(t.Amount))
            + transactions.Where(t => t.Type == SecurityPostingSubType.Fee
                                       && !sellGroupIds.Contains(t.GroupId))
                          .Sum(t => Math.Abs(t.Amount));

        // FIFO cost basis of currently held shares only — see ComputeReturnSummaryAsync for full rationale.
        decimal investedCapital = fifoResult.TotalCostBasis - fifoResult.StandaloneFeeTotal;

        decimal grossReturn = currentMarketValue + totalSalesProceeds + grossDividends - historicalInvestedCapital;
        decimal netReturn = currentMarketValue + totalSalesProceeds + netDividends - historicalInvestedCapital;
        decimal taxRate = _calc.CalculateTaxRate(totalTaxes, grossReturn) ?? 0m;

        // TWR
        var twrPeriods = BuildTwrPeriods(transactions, filledPrices);
        decimal? twr = _calc.CalculateTwr(twrPeriods);

        // Volatility & MaxDrawdown from portfolio values
        var portfolioValues = BuildPortfolioValueSeries(transactions, filledPrices);
        decimal? volatility = portfolioValues.Count >= 2
            ? _calc.CalculateVolatility(portfolioValues)
            : null;
        decimal? maxDrawdown = portfolioValues.Count >= 2
            ? _calc.CalculateMaxDrawdown(portfolioValues)
            : null;

        // Sharpe Ratio (opt-in)
        decimal? sharpeRatio = null;
        if (user.ShowSharpeRatio && twr.HasValue && volatility.HasValue && volatility.Value > 0m)
        {
            sharpeRatio = _calc.CalculateSharpeRatio(twr.Value, user.RiskFreeRate, volatility.Value);
        }

        decimal unrealizedGains = currentMarketValue - investedCapital;

        // IRR
        var irrCashflows = BuildIrrCashflows(transactions, currentMarketValue, DateTime.Today);
        decimal? irr = _calc.CalculateIrr(irrCashflows);

        // Dividend yield current year
        int currentYear = DateTime.Today.Year;
        decimal currentYearDividends = transactions
            .Where(t => t.Type == SecurityPostingSubType.Dividend && t.Date.Year == currentYear)
            .Sum(t => t.Amount);
        decimal dividendYieldCurrentYear = _calc.CalculateDividendYield(currentYearDividends, investedCapital) ?? 0m;

        return new DetailedReturnMetricsDto(
            GrossReturn: grossReturn,
            NetReturn: netReturn,
            TotalTaxes: totalTaxes,
            TotalFees: totalFees,
            TaxRate: taxRate,
            Twr: twr,
            Volatility: volatility,
            MaxDrawdown: maxDrawdown,
            SharpeRatio: sharpeRatio,
            RealizedGains: fifoResult.RealizedGains,
            UnrealizedGains: unrealizedGains,
            Irr: irr,
            DividendYieldCurrentYear: dividendYieldCurrentYear);
    }

    private async Task<PeriodicReturnsDto?> ComputePeriodicReturnsAsync(
        Guid securityId, Guid ownerUserId, CancellationToken ct)
    {
        bool securityExists = await _db.Securities
            .AsNoTracking()
            .AnyAsync(s => s.Id == securityId && s.OwnerUserId == ownerUserId, ct);

        if (!securityExists) return null;

        var transactions = await LoadPostingsAsync(securityId, ownerUserId, ct);
        if (transactions.Count == 0) return null;

        var firstDate = transactions.Min(t => t.Date).Date;
        var prices = await LoadPriceHistoryAsync(securityId, ownerUserId, firstDate, DateTime.Today, ct);

        // Fall back to transaction-implied prices (linearly interpolated between buy/sell anchors)
        // when no real price data exists, so that portfolio values are never all-zero.
        var simulatedPrices = SimulatePricesFromTransactions(transactions);

        // Simulated prices are actually used when they predate the first real price entry,
        // or when there are no real prices at all.
        bool hasSimulatedPrices = simulatedPrices.Count > 0
            && (prices.Count == 0 || simulatedPrices.Any(p => p.Date.Date < prices[0].Date.Date));

        var mergedPrices = MergeSimulatedAndRealPrices(simulatedPrices, prices);
        var filledPrices = ForwardFill(mergedPrices, firstDate, DateTime.Today);

        int currentYear = DateTime.Today.Year;
        int firstYear = firstDate.Year;

        // When the position is fully closed, only compute returns up to the last transaction year
        // to avoid showing empty years after the final sale.
        decimal sharesHeldToday = ComputeSharesHeldOnDate(transactions, DateTime.Today);
        int lastYear = sharesHeldToday > 0m
            ? currentYear
            : transactions.Max(t => t.Date).Year;

        var annualReturns = new List<AnnualReturnPoint>();
        var monthlyReturns = new List<MonthlyReturnPoint>();

        for (int year = firstYear; year <= lastYear; year++)
        {
            // isYtd only applies to the current calendar year while the position is still open
            bool isYtd = year == currentYear && sharesHeldToday > 0m;

            var yearStartDate = new DateTime(year, 1, 1);
            var yearEndDate = isYtd ? DateTime.Today : new DateTime(year, 12, 31);

            // Use Modified Dietz to correctly account for cash flows (buys, sells, fees)
            // within the period. Returns null when no capital is deployed (denominator = 0).
            decimal? annualReturn = ComputeModifiedDietz(transactions, filledPrices, yearStartDate, yearEndDate);
            annualReturns.Add(new AnnualReturnPoint(year, annualReturn ?? 0m, isYtd));

            int lastMonth = isYtd ? DateTime.Today.Month : 12;
            for (int month = 1; month <= lastMonth; month++)
            {
                var monthStart = new DateTime(year, month, 1);
                var monthEnd = isYtd && month == DateTime.Today.Month
                    ? DateTime.Today
                    : new DateTime(year, month, DateTime.DaysInMonth(year, month));

                decimal? monthReturn = ComputeModifiedDietz(transactions, filledPrices, monthStart, monthEnd);
                monthlyReturns.Add(new MonthlyReturnPoint(year, month, monthReturn));
            }
        }

        // Annual dividends
        var annualDividends = BuildAnnualDividends(transactions, firstYear, lastYear);

        return new PeriodicReturnsDto(
            AnnualReturns: annualReturns.AsReadOnly(),
            MonthlyReturns: monthlyReturns.AsReadOnly(),
            AnnualDividends: annualDividends,
            HasSimulatedPrices: hasSimulatedPrices);
    }

    /// <summary>
    /// Computes the Modified Dietz return for a given period, correctly accounting for
    /// external cash flows (buys, sells, fees) that occur within the period.
    /// <para>
    /// Unlike simple HPR, Modified Dietz handles periods where the starting or ending
    /// portfolio value is zero (e.g. the year of first purchase or the year of full sale).
    /// </para>
    /// </summary>
    /// <param name="transactions">All transactions for the security.</param>
    /// <param name="filledPrices">Forward-filled daily price series.</param>
    /// <param name="periodStart">First day of the period (inclusive).</param>
    /// <param name="periodEnd">Last day of the period (inclusive).</param>
    /// <returns>
    /// Return as a decimal fraction (e.g. 0.05 = 5%), or <c>null</c> when no capital
    /// is deployed in the period (denominator is zero).
    /// </returns>
    private static decimal? ComputeModifiedDietz(
        IReadOnlyList<SecurityTransaction> transactions,
        IReadOnlyList<(DateTime Date, decimal Close)> filledPrices,
        DateTime periodStart,
        DateTime periodEnd)
    {
        // V_start: value at end of the day before the period starts (excludes same-day cash flows)
        decimal startValue = GetPortfolioValueOnDate(transactions, filledPrices, periodStart.AddDays(-1));
        // V_end: value at end of the last day of the period (includes same-day cash flows)
        decimal endValue = GetPortfolioValueOnDate(transactions, filledPrices, periodEnd);

        int totalDays = Math.Max(1, (int)(periodEnd.Date - periodStart.Date).TotalDays);

        decimal cfNet = 0m;
        decimal cfWeighted = 0m;

        foreach (var tx in transactions)
        {
            if (tx.Date.Date < periodStart.Date || tx.Date.Date > periodEnd.Date) continue;
            if (tx.Type != SecurityPostingSubType.Buy
                && tx.Type != SecurityPostingSubType.Sell
                && tx.Type != SecurityPostingSubType.Fee) continue;

            // Sign convention (from the investor's perspective):
            //   Buy / Fee: money flows INTO the position → positive cash flow
            //              Amount is stored as negative in DB, so use Math.Abs
            //   Sell:      money flows OUT of the position → negative cash flow
            //              Amount is stored as positive in DB, so negate
            decimal cf = tx.Type == SecurityPostingSubType.Sell
                ? -tx.Amount
                : Math.Abs(tx.Amount);

            // Weight = fraction of period remaining when the cash flow occurred
            int dayIndex = (int)(tx.Date.Date - periodStart.Date).TotalDays;
            decimal weight = (decimal)(totalDays - dayIndex) / totalDays;

            cfNet += cf;
            cfWeighted += cf * weight;
        }

        decimal denominator = startValue + cfWeighted;
        if (denominator == 0m) return null;

        return (endValue - startValue - cfNet) / denominator;
    }

    private async Task<CashflowTimelineDto?> ComputeCashflowTimelineAsync(
        Guid securityId, Guid ownerUserId, CancellationToken ct)
    {
        bool securityExists = await _db.Securities
            .AsNoTracking()
            .AnyAsync(s => s.Id == securityId && s.OwnerUserId == ownerUserId, ct);

        if (!securityExists) return null;

        var transactions = await LoadPostingsAsync(securityId, ownerUserId, ct);
        if (transactions.Count == 0)
            return new CashflowTimelineDto(
                Array.Empty<CashflowEntry>(),
                Array.Empty<AnnualCashflowSummary>());

        var entries = transactions.Select(t => new CashflowEntry(
            Date: t.Date,
            Type: t.Type.ToString(),
            Amount: t.Amount,
            Description: null,
            PostingId: t.Id)).ToList();

        // Annual summaries
        var annualGroups = transactions
            .GroupBy(t => t.Date.Year)
            .OrderBy(g => g.Key);

        var annualSummaries = annualGroups.Select(g =>
        {
            decimal totalBuys = g.Where(t => t.Type == SecurityPostingSubType.Buy).Sum(t => t.Amount);
            decimal totalSells = g.Where(t => t.Type == SecurityPostingSubType.Sell).Sum(t => t.Amount);
            decimal totalDividends = g.Where(t => t.Type == SecurityPostingSubType.Dividend).Sum(t => t.Amount);
            decimal totalTaxes = g.Where(t => t.Type == SecurityPostingSubType.Tax).Sum(t => t.Amount);
            decimal totalFees = g.Where(t => t.Type == SecurityPostingSubType.Fee).Sum(t => t.Amount);

            return new AnnualCashflowSummary(g.Key, totalBuys, totalSells, totalDividends, totalTaxes, totalFees);
        }).ToList();

        return new CashflowTimelineDto(entries.AsReadOnly(), annualSummaries.AsReadOnly());
    }

    private async Task<PerformanceChartDataDto?> ComputePerformanceChartDataAsync(
        Guid securityId, Guid ownerUserId, ChartTimeRange timeRange, CancellationToken ct)
    {
        bool securityExists = await _db.Securities
            .AsNoTracking()
            .AnyAsync(s => s.Id == securityId && s.OwnerUserId == ownerUserId, ct);

        if (!securityExists) return null;

        var transactions = await LoadPostingsAsync(securityId, ownerUserId, ct);
        if (transactions.Count == 0) return null;

        var fromDate = GetFromDateForTimeRange(timeRange);
        var prices = await LoadPriceHistoryAsync(securityId, ownerUserId, fromDate, DateTime.Today, ct);

        // Simulate prices from transaction implied prices (|Amount| / |Quantity|) with linear
        // interpolation between anchors. Used to fill gaps before real price history begins.
        var simulatedPrices = SimulatePricesFromTransactions(transactions);

        // Merge: simulated prices cover gaps before the first real price entry so that the
        // chart always starts from the first buy, even when the price feed begins later.
        var mergedPrices = MergeSimulatedAndRealPrices(simulatedPrices, prices);

        // Determine the effective chart start: use the first available price (simulated or real),
        // bounded from below by the time-range filter only when it has a meaningful date.
        var effectiveFrom = mergedPrices.Count > 0 ? mergedPrices[0].Date : fromDate;
        if (fromDate > DateTime.MinValue && fromDate > effectiveFrom)
        {
            effectiveFrom = fromDate;
        }

        var filledPrices = ForwardFill(mergedPrices, effectiveFrom, DateTime.Today);
        if (filledPrices.Count == 0) return null;

        var portfolioValues = new List<ChartPoint>(filledPrices.Count);
        var investedCapitalValues = new List<ChartPoint>(filledPrices.Count);

        foreach (var (date, close) in filledPrices)
        {
            decimal sharesHeld = ComputeSharesHeldOnDate(transactions, date);
            decimal marketValue = sharesHeld * close;
            decimal investedCapital = ComputeInvestedCapitalOnDate(transactions, date);

            portfolioValues.Add(new ChartPoint(date, marketValue));
            investedCapitalValues.Add(new ChartPoint(date, investedCapital));
        }

        return new PerformanceChartDataDto(
            TimeRange: timeRange,
            PortfolioValues: portfolioValues.AsReadOnly(),
            InvestedCapitalValues: investedCapitalValues.AsReadOnly());
    }

    private async Task<BenchmarkComparisonDto?> ComputeBenchmarkComparisonAsync(
        Guid securityId, Guid ownerUserId, CancellationToken ct)
    {
        var user = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == ownerUserId, ct);

        if (user?.BenchmarkSecurityId == null) return null;

        var benchmarkId = user.BenchmarkSecurityId.Value;

        // S-3: Verify benchmark security is owned by the same user
        var benchmarkSecurity = await _db.Securities
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == benchmarkId && s.OwnerUserId == ownerUserId, ct);

        if (benchmarkSecurity == null)
        {
            _logger.LogWarning(
                "ComputeBenchmarkComparisonAsync: Benchmark security {BenchmarkId} not found or not owned by user {UserId}.",
                benchmarkId, ownerUserId);
            return null;
        }

        // Load price history for the target security (from first transaction)
        var transactions = await LoadPostingsAsync(securityId, ownerUserId, ct);
        if (transactions.Count == 0) return null;

        var firstDate = transactions.Min(t => t.Date).Date;

        // When the security is fully sold, end the chart at the last sell date instead of today
        var sharesHeldToday = ComputeSharesHeldOnDate(transactions, DateTime.Today);
        DateTime endDate;
        if (sharesHeldToday == 0m)
        {
            // Find the last sell transaction that brought shares to zero
            var lastSell = transactions
                .Where(t => t.Type == SecurityPostingSubType.Sell)
                .OrderByDescending(t => t.Date)
                .FirstOrDefault();
            endDate = lastSell?.Date.Date ?? DateTime.Today;
        }
        else
        {
            endDate = DateTime.Today;
        }

        var securityPrices = await LoadPriceHistoryAsync(securityId, ownerUserId, firstDate, endDate, ct);
        if (securityPrices.Count < 2) return null;

        // Load benchmark price history for the same period
        var benchmarkPrices = await LoadPriceHistoryAsync(benchmarkId, ownerUserId, firstDate, endDate, ct);
        if (benchmarkPrices.Count < 2) return null;

        // Normalize both to base 100 at the first date the benchmark has price data
        var securityFilled = ForwardFill(securityPrices, firstDate, endDate);
        var benchmarkFilled = ForwardFill(benchmarkPrices, firstDate, endDate);

        if (securityFilled.Count == 0 || benchmarkFilled.Count == 0) return null;

        // The comparison starts at the earliest date the benchmark actually has price data.
        // The security may have data earlier (context zone before the benchmark was available).
        var comparisonStart = benchmarkFilled[0].Date.Date;

        // Find the security's price at comparisonStart for common normalization base.
        var securityAtComparison = securityFilled.Find(p => p.Date.Date >= comparisonStart);
        if (securityAtComparison == default) return null;

        decimal securityCompBase = securityAtComparison.Close;
        decimal benchmarkCompBase = benchmarkFilled[0].Close;

        if (securityCompBase == 0m || benchmarkCompBase == 0m) return null;

        // Security: full period normalized at comparisonStart.
        // Values before comparisonStart reflect the security's performance relative to that anchor
        // (i.e., values < 100 show where the security was relative to its comparisonStart price).
        var securityNormalized = securityFilled
            .Select(p => new ChartPoint(p.Date, p.Close / securityCompBase * 100m))
            .ToList();

        // Benchmark: from comparisonStart onward, starts at 100.
        var benchmarkNormalized = benchmarkFilled
            .Select(p => new ChartPoint(p.Date, p.Close / benchmarkCompBase * 100m))
            .ToList();

        // Pre-overlap benchmark values: forward-fill the raw benchmark prices in [firstDate, comparisonStart).
        // These use the same normalization base so the curve connects seamlessly at comparisonStart = 100.
        // If no benchmark prices exist before comparisonStart, the list is empty and the chart falls back
        // to a flat reference line.
        List<ChartPoint> benchmarkPreOverlap = [];
        if (comparisonStart > firstDate)
        {
            var preOverlapEnd = comparisonStart.AddDays(-1);
            var preOverlapFilled = ForwardFill(benchmarkPrices, firstDate, preOverlapEnd);
            benchmarkPreOverlap = preOverlapFilled
                .Select(p => new ChartPoint(p.Date, p.Close / benchmarkCompBase * 100m))
                .ToList();
        }

        return new BenchmarkComparisonDto(
            BenchmarkSecurityId: benchmarkId,
            BenchmarkName: benchmarkSecurity.Name,
            SecurityNormalizedValues: securityNormalized.AsReadOnly(),
            BenchmarkNormalizedValues: benchmarkNormalized.AsReadOnly(),
            BenchmarkPreOverlapValues: benchmarkPreOverlap.AsReadOnly(),
            StartDate: firstDate,
            ComparisonStartDate: comparisonStart,
            EndDate: endDate);
    }

    /// <summary>
    /// Computes per-KPI formula breakdowns for all widget KPIs (FR-1 side panel).
    /// Enforces user-scoping via <see cref="LoadPostingsAsync"/> (S-1).
    /// </summary>
    /// <param name="securityId">The security id.</param>
    /// <param name="ownerUserId">The owner user id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    private async Task<IReadOnlyList<KpiBreakdownDto>?> ComputeKpiBreakdownsAsync(
        Guid securityId, Guid ownerUserId, CancellationToken ct)
    {
        var security = await _db.Securities
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == securityId && s.OwnerUserId == ownerUserId, ct);

        if (security == null) return null;

        var transactions = await LoadPostingsAsync(securityId, ownerUserId, ct);
        if (transactions.Count == 0) return null;

        var fifoResult = _fifo.Calculate(transactions);

        var latestPriceRow = await _db.SecurityPrices
            .AsNoTracking()
            .Where(sp => sp.SecurityId == securityId)
            .OrderByDescending(sp => sp.Date)
            .Select(sp => sp.Close)
            .FirstOrDefaultAsync(ct);

        decimal currentPrice = latestPriceRow;
        decimal sharesHeld = fifoResult.TotalSharesHeld;
        decimal currentMarketValue = sharesHeld * currentPrice;

        // FIFO cost basis of currently held shares only (= 0 when position is fully sold).
        // StandaloneFeeTotal covers fees not linked to any buy lot (including sell-linked fees).
        decimal displayInvestedCapital = fifoResult.TotalCostBasis - fifoResult.StandaloneFeeTotal;

        // Categorised transactions
        var buys = transactions.Where(t => t.Type == SecurityPostingSubType.Buy).ToList();
        var sells = transactions.Where(t => t.Type == SecurityPostingSubType.Sell).ToList();
        var dividends = transactions.Where(t => t.Type == SecurityPostingSubType.Dividend).ToList();
        var taxes = transactions.Where(t => t.Type == SecurityPostingSubType.Tax).ToList();
        var fees = transactions.Where(t => t.Type == SecurityPostingSubType.Fee).ToList();

        decimal grossDividends = dividends.Sum(t => t.Amount);
        decimal totalTaxes = taxes.Sum(t => Math.Abs(t.Amount));
        decimal netDividends = grossDividends - totalTaxes;
        DateTime firstBuyDate = buys.Count > 0 ? buys.Min(t => t.Date) : DateTime.Today;

        // When position is fully sold, investment period ends at last sell date — not today.
        DateTime cagrEndDate = sharesHeld <= 0m && sells.Count > 0
            ? sells.Max(t => t.Date)
            : DateTime.Today;

        // ── Build per-buy item list ───────────────────────────────────────────
        // Used by both the TotalReturn ("Kursgewinne / -verluste") and the InvestedCapital ("Käufe") KPIs.
        // Each buy posting becomes its own item. Fees that share a GroupId with a buy (linked fees) are
        // placed immediately after that buy. Standalone fees (GroupId == Guid.Empty or no matching buy)
        // are appended at the end of the list.
        // Amounts are stored as positive values; the TotalReturn variant negates them (outflows).
        var buyGroupIds = buys
            .Where(b => b.GroupId != Guid.Empty)
            .Select(b => b.GroupId)
            .ToHashSet();
        var feesByGroupId = fees.ToLookup(f => f.GroupId);

        // GroupIds associated with sell transactions – used to exclude sell-linked fees from buysGroupItemsList
        var sellGroupIds = sells.Where(s => s.GroupId != Guid.Empty).Select(s => s.GroupId).ToHashSet();

        var buysGroupItemsList = new List<KpiBreakdownItem>();
        foreach (var buy in buys)
        {
            decimal absAmt = Math.Abs(buy.Amount);
            decimal pricePerShare = buy.Quantity is > 0 ? absAmt / buy.Quantity.Value : 0m;
            buysGroupItemsList.Add(new KpiBreakdownItem(
                buy.Date,
                absAmt,
                _localizer.Format("Item_BuySharesAtPrice", buy.Quantity?.ToString("G6") ?? "0", pricePerShare.ToString("N2"))));

            if (buy.GroupId != Guid.Empty)
            {
                foreach (var fee in feesByGroupId[buy.GroupId])
                {
                    buysGroupItemsList.Add(new KpiBreakdownItem(fee.Date, Math.Abs(fee.Amount), _localizer["Item_Fee"]));
                }
            }
        }

        // Standalone fees: not linked to any buy AND not linked to any sell
        foreach (var fee in fees.Where(f => (f.GroupId == Guid.Empty || !buyGroupIds.Contains(f.GroupId)) && !sellGroupIds.Contains(f.GroupId)))
        {
            buysGroupItemsList.Add(new KpiBreakdownItem(fee.Date, Math.Abs(fee.Amount), _localizer["Item_StandaloneFee"]));
        }

        // ── Sell net proceeds: gross sell amount with linked fees as sub-items ──────────────
        // GroupTotal = totalSalesProceeds (net), individual items show gross + fee breakdown.
        var sellNetItems = new List<KpiBreakdownItem>();
        decimal totalSalesProceeds = 0m;
        foreach (var sell in sells)
        {
            decimal linkedFees = sell.GroupId != Guid.Empty
                ? feesByGroupId[sell.GroupId].Sum(f => Math.Abs(f.Amount))
                : 0m;
            totalSalesProceeds += sell.Amount - linkedFees;
            decimal absQty = sell.Quantity.HasValue ? Math.Abs(sell.Quantity.Value) : 0m;
            decimal pricePerShare = absQty > 0 ? sell.Amount / absQty : sell.Amount;
            string sellLabel = absQty > 0
                ? _localizer.Format("Item_SellWithDetails", absQty.ToString("G6"), pricePerShare.ToString("N2"))
                : _localizer["Item_Sell"];
            sellNetItems.Add(new KpiBreakdownItem(sell.Date, sell.Amount, sellLabel));
            if (sell.GroupId != Guid.Empty)
            {
                foreach (var fee in feesByGroupId[sell.GroupId])
                {
                    sellNetItems.Add(new KpiBreakdownItem(fee.Date, -Math.Abs(fee.Amount), _localizer["Item_Fee"]));
                }
            }
        }

        // Historical invested capital: total buy outflows + fees not linked to sells.
        // Used as denominator for TotalReturn% and CAGR (matches ComputeReturnSummaryAsync).
        decimal historicalInvestedCapital =
            transactions.Where(t => t.Type == SecurityPostingSubType.Buy).Sum(t => Math.Abs(t.Amount))
            + transactions.Where(t => t.Type == SecurityPostingSubType.Fee && !sellGroupIds.Contains(t.GroupId))
                          .Sum(t => Math.Abs(t.Amount));

        decimal totalReturnAbsolute = currentMarketValue + totalSalesProceeds + netDividends - historicalInvestedCapital;
        decimal? totalReturnPercent = _calc.CalculateTotalReturn(historicalInvestedCapital, currentMarketValue + totalSalesProceeds, netDividends);

        // Build mixed dividends + taxes list for the "Dividenden (netto)" group in TotalReturn.        // Taxes that share a GroupId with a dividend are placed directly after that dividend.
        // Remaining taxes (standalone or unmatched GroupId) are appended at the end, sorted by date.
        var dividendGroupIds = dividends
            .Where(d => d.GroupId != Guid.Empty)
            .Select(d => d.GroupId)
            .ToHashSet();
        var taxesByGroupId = taxes.ToLookup(t => t.GroupId);
        var netDividendItems = new List<KpiBreakdownItem>();
        foreach (var div in dividends)
        {
            netDividendItems.Add(new KpiBreakdownItem(div.Date, div.Amount, null));
            if (div.GroupId != Guid.Empty)
            {
                foreach (var tax in taxesByGroupId[div.GroupId])
                {
                    netDividendItems.Add(new KpiBreakdownItem(tax.Date, -Math.Abs(tax.Amount), _localizer["Item_Tax"]));
                }
            }
        }

        foreach (var tax in taxes
            .Where(t => t.GroupId == Guid.Empty || !dividendGroupIds.Contains(t.GroupId))
            .OrderBy(t => t.Date))
        {
            netDividendItems.Add(new KpiBreakdownItem(tax.Date, -Math.Abs(tax.Amount), _localizer["Item_Tax"]));
        }

        // ── IRR: compute value and build cashflow-timeline items ─────────────
        var irrCashflowsForCalc = BuildIrrCashflows(transactions, currentMarketValue, DateTime.Today);
        decimal? irrValue = _calc.CalculateIrr(irrCashflowsForCalc);

        var irrTimelineItems = BuildIrrTimelineItems(
            buys, dividends, sells, taxes, fees,
            feesByGroupId, taxesByGroupId,
            currentMarketValue,
            irrValue);

        // Sum of present values – by IRR definition this equals 0 when IRR is computed correctly.
        decimal sumOfPresentValues = irrValue.HasValue ? irrTimelineItems.Sum(i => i.Amount) : 0m;

        // GroupNote for the cashflow-timeline group: display the IRR rate so the user can see r.
        string irrTimelineGroupNote = irrValue.HasValue
            ? _localizer.Format("Group_Irr_RateNote", (irrValue.Value * 100m).ToString("N2"))
            : _localizer["Group_Irr_SignNote"];

        // ── Build TotalReturn groups (conditional sells group when there are sales) ──────────
        var totalReturnGroups = new List<KpiFormulaGroup>
        {
            new(
                GroupName: _localizer["Group_CurrentMarketValue"],
                IsPositiveContribution: true,
                Items: [new KpiBreakdownItem(DateTime.Today, currentMarketValue, _localizer.Format("Item_SharesTimesPrice", sharesHeld.ToString("G6"), currentPrice.ToString("N2")))],
                GroupTotal: currentMarketValue),
        };
        if (sells.Count > 0)
        {
            totalReturnGroups.Add(new KpiFormulaGroup(
                GroupName: _localizer["Group_SalesProceeds_Net"],
                IsPositiveContribution: totalSalesProceeds >= 0,
                Items: sellNetItems,
                GroupTotal: totalSalesProceeds));
        }

        totalReturnGroups.Add(new KpiFormulaGroup(
            GroupName: _localizer["Group_InvestedCapital"],
            IsPositiveContribution: false,
            Items: buysGroupItemsList.Select(it => it with { Amount = -it.Amount }).ToList(),
            GroupTotal: -historicalInvestedCapital));
        totalReturnGroups.Add(new KpiFormulaGroup(
            GroupName: _localizer["Group_DividendsNet"],
            IsPositiveContribution: true,
            Items: netDividendItems,
            GroupTotal: netDividends));
        totalReturnGroups.Add(new KpiFormulaGroup(
            GroupName: _localizer["Group_TotalReturn_Result"],
            IsPositiveContribution: totalReturnAbsolute >= 0,
            Items: [new KpiBreakdownItem(DateTime.Today, totalReturnAbsolute, _localizer["Item_TotalReturn_Note"])],
            GroupTotal: totalReturnAbsolute,
            GroupTotalPercent: totalReturnPercent));

        // ── InvestedCapital: FIFO cost basis of currently held shares (remaining lots) ──────
        var investedCapitalItems = new List<KpiBreakdownItem>();
        if (sharesHeld <= 0m || fifoResult.RemainingLots.Count == 0)
        {
            investedCapitalItems.Add(new KpiBreakdownItem(DateTime.Today, 0m, _localizer["Item_PositionFullySold"]));
        }
        else
        {
            foreach (var lot in fifoResult.RemainingLots)
            {
                decimal lotCost = lot.Quantity * lot.CostPerUnit;
                investedCapitalItems.Add(new KpiBreakdownItem(
                    lot.PurchaseDate,
                    lotCost,
                    _localizer.Format("Item_BuySharesAtPrice", lot.Quantity.ToString("G6"), lot.CostPerUnit.ToString("N2"))));
            }
        }

        var result = new List<KpiBreakdownDto>
        {
            // ── Gesamtrendite (Total Return) ──────────────────────────────────
            // Formel: Total Return = Marktwert + Verkaufserlöse (netto) − Investiertes Kapital (historisch) + (Dividenden − Steuern)
            new KpiBreakdownDto(
                KpiKey: "TotalReturn",
                DisplayName: _localizer["Kpi_TotalReturn_DisplayName"],
                FormulaText: _localizer["Kpi_TotalReturn_Formula"],
                Description: _localizer["Kpi_TotalReturn_Description"],
                Groups: totalReturnGroups),

            // ── Marktwert (Current Market Value) ────────────────────────────
            new KpiBreakdownDto(
                KpiKey: "MarketValue",
                DisplayName: _localizer["Kpi_MarketValue_DisplayName"],
                FormulaText: _localizer["Kpi_MarketValue_Formula"],
                Description: _localizer["Kpi_MarketValue_Description"],
                Groups:
                [
                    new KpiFormulaGroup(
                        GroupName: _localizer["Group_Buys_WithShares"],
                        IsPositiveContribution: true,
                        Items: buys
                            .Select(t => new KpiBreakdownItem(
                                t.Date,
                                Math.Abs(t.Amount),
                                t.Quantity.HasValue ? _localizer.Format("Item_AddShares", t.Quantity.Value.ToString("N4")) : null))
                            .ToList(),
                        GroupTotal: buys.Sum(t => Math.Abs(t.Amount))),
                    new KpiFormulaGroup(
                        GroupName: _localizer["Group_Sells_WithShares"],
                        IsPositiveContribution: false,
                        Items: sells
                            .Select(t => new KpiBreakdownItem(
                                t.Date,
                                t.Amount,
                                t.Quantity.HasValue ? _localizer.Format("Item_SubtractShares", t.Quantity.Value.ToString("N4")) : null))
                            .ToList(),
                        GroupTotal: sells.Sum(t => t.Amount)),
                ]),

            // ── Investiertes Kapital ─────────────────────────────────────────
            // Shows FIFO cost basis of currently held shares (remaining lots).
            // When position is fully sold, displays a "Position vollständig verkauft" note.
            new KpiBreakdownDto(
                KpiKey: "InvestedCapital",
                DisplayName: _localizer["Kpi_InvestedCapital_DisplayName"],
                FormulaText: _localizer["Kpi_InvestedCapital_Formula"],
                Description: _localizer["Kpi_InvestedCapital_Description"],
                Groups:
                [
                    new KpiFormulaGroup(
                        GroupName: _localizer["Group_RemainingFifoCostBasis"],
                        IsPositiveContribution: true,
                        Items: investedCapitalItems,
                        GroupTotal: displayInvestedCapital),
                ]),

            // ── CAGR ─────────────────────────────────────────────────────────
            // Formel: CAGR = ((Marktwert + Verkaufserlöse + Nettodividenden) / Investiertes Kapital) ^ (1 / Jahre) − 1
            // Jahre = (cagrEndDate − firstBuyDate).TotalDays / 365.25
            // cagrEndDate = last sell date if fully sold, otherwise today
            BuildCagrBreakdown(
                firstBuyDate,
                cagrEndDate,
                sharesHeld,
                currentPrice,
                netDividendItems,
                netDividends,
                buysGroupItemsList,
                historicalInvestedCapital,
                currentMarketValue,
                sellNetItems,
                totalSalesProceeds),

            // ── IRR (persönliche Rendite) ─────────────────────────────────────
            new KpiBreakdownDto(
                KpiKey: "Irr",
                DisplayName: _localizer["Kpi_Irr_DisplayName"],
                FormulaText: _localizer["Kpi_Irr_Formula"],
                Description: _localizer["Kpi_Irr_Description"],
                Groups:
                [
                    new KpiFormulaGroup(
                        GroupName: _localizer["Group_IrrCashflows"],
                        IsPositiveContribution: true,
                        Items: irrTimelineItems,
                        GroupTotal: sumOfPresentValues,
                        GroupNote: irrTimelineGroupNote),
                    new KpiFormulaGroup(
                        GroupName: _localizer["Group_IrrProbe"],
                        IsPositiveContribution: true,
                        Items: [],
                        GroupTotal: sumOfPresentValues,
                        GroupNote: Math.Abs(sumOfPresentValues) < 0.01m
                            ? _localizer["Group_IrrProbe_OK"]
                            : _localizer.Format("Group_IrrProbe_Rounding", sumOfPresentValues.ToString("N2"))),
                    new KpiFormulaGroup(
                        GroupName: _localizer["Group_Irr_Result"],
                        IsPositiveContribution: irrValue.HasValue && irrValue.Value >= 0m,
                        Items: [],
                        GroupTotal: 0m,
                        GroupTotalPercent: irrValue),
                ]),
        };

        return result.AsReadOnly();
    }


    /// <summary>
    /// Builds the CAGR <see cref="KpiBreakdownDto"/> with formula groups:
    /// Marktwert, (Verkaufserlöse), Nettodividenden, Investiertes Kapital, Anlagedauer, Ergebnis (CAGR).
    /// </summary>
    /// <param name="firstBuyDate">Date of the earliest buy transaction.</param>
    /// <param name="cagrEndDate">
    /// End date of the investment period: last sell date if position is fully sold, otherwise today.
    /// </param>
    /// <param name="sharesHeld">Number of shares currently held.</param>
    /// <param name="currentPrice">Current price per share.</param>
    /// <param name="netDividendItems">Pre-built dividend + linked-tax item list (shared with TotalReturn).</param>
    /// <param name="netDividends">Net dividends amount (gross dividends − taxes).</param>
    /// <param name="buysGroupItemsList">Pre-built buy + linked-fee item list with positive amounts.</param>
    /// <param name="historicalInvestedCapital">Total historical buy outflows + non-sell fees (denominator for CAGR).</param>
    /// <param name="currentMarketValue">Current market value (shares held × current price).</param>
    /// <param name="sellNetItems">Pre-built sell item list for the conditional sales proceeds group.</param>
    /// <param name="totalSalesProceeds">Net sales proceeds (gross sell amounts minus sell-linked fees).</param>
    /// <returns>The result.</returns>
    private KpiBreakdownDto BuildCagrBreakdown(
        DateTime firstBuyDate,
        DateTime cagrEndDate,
        decimal sharesHeld,
        decimal currentPrice,
        List<KpiBreakdownItem> netDividendItems,
        decimal netDividends,
        List<KpiBreakdownItem> buysGroupItemsList,
        decimal historicalInvestedCapital,
        decimal currentMarketValue,
        List<KpiBreakdownItem> sellNetItems,
        decimal totalSalesProceeds)
    {
        // ── Gruppe 1: Marktwert – single item (total shares held × current price) ──────────
        var marktwertItems = new List<KpiBreakdownItem>
        {
            new(DateTime.Today, currentMarketValue, _localizer.Format("Item_SharesTimesPrice", sharesHeld.ToString("G6"), currentPrice.ToString("N2"))),
        };

        // ── Gruppe 4: Anlagedauer – informational text items (no EUR amounts) ────────────────
        double cagrYears = (cagrEndDate.Date - firstBuyDate.Date).TotalDays / 365.25;

        int fullYears = cagrEndDate.Year - firstBuyDate.Year;
        int monthDiff = cagrEndDate.Month - firstBuyDate.Month;
        if (monthDiff < 0 || (monthDiff == 0 && cagrEndDate.Day < firstBuyDate.Day))
        {
            fullYears--;
            monthDiff = (monthDiff + 12) % 12;
        }
        string durationText = _localizer.Format("Item_HoldingPeriodYearsMonths", fullYears, monthDiff);

        // When position is fully sold, the end item shows the last sell date; otherwise today.
        string endDateLabel = cagrEndDate.Date == DateTime.Today.Date
            ? _localizer["Item_Today"]
            : _localizer["Item_LastSell"];

        var anlagedauerItems = new List<KpiBreakdownItem>
        {
            new(firstBuyDate, 0m, _localizer["Item_FirstBuy"]),
            new(cagrEndDate, 0m, endDateLabel),
            new(cagrEndDate, 0m, _localizer.Format("Item_Period", durationText)),
        };

        // ── Gruppe 5: CAGR-Ergebnis ────────────────────────────────────────────────────────────
        decimal? cagrValue;
        if (cagrYears <= 0 || historicalInvestedCapital <= 0)
        {
            cagrValue = 0m;
        }
        else
        {
            cagrValue = _calc.CalculateCagr(historicalInvestedCapital, currentMarketValue + totalSalesProceeds + netDividends, cagrYears);
        }

        // ── Build CAGR groups (conditional sells group) ───────────────────────────────────────
        var cagrGroups = new List<KpiFormulaGroup>
        {
            new(
                GroupName: _localizer["Group_MarketValue"],
                IsPositiveContribution: true,
                Items: marktwertItems,
                GroupTotal: currentMarketValue),
        };
        if (sellNetItems.Count > 0)
        {
            cagrGroups.Add(new KpiFormulaGroup(
                GroupName: _localizer["Group_SalesProceeds_Net"],
                IsPositiveContribution: totalSalesProceeds >= 0,
                Items: sellNetItems,
                GroupTotal: totalSalesProceeds));
        }

        cagrGroups.Add(new KpiFormulaGroup(
            GroupName: _localizer["Group_NetDividends"],
            IsPositiveContribution: true,
            Items: netDividendItems,
            GroupTotal: netDividends));
        cagrGroups.Add(new KpiFormulaGroup(
            GroupName: _localizer["Group_InvestedCapital"],
            IsPositiveContribution: false,
            Items: buysGroupItemsList,
            GroupTotal: historicalInvestedCapital));
        cagrGroups.Add(new KpiFormulaGroup(
            GroupName: _localizer["Group_HoldingPeriod"],
            IsPositiveContribution: true,
            Items: anlagedauerItems,
            GroupTotal: 0m,
            GroupNote: durationText));
        cagrGroups.Add(new KpiFormulaGroup(
            GroupName: _localizer["Group_Cagr_Result"],
            IsPositiveContribution: cagrValue.HasValue && cagrValue.Value >= 0m,
            Items: [],
            GroupTotal: 0m,
            GroupTotalPercent: cagrValue));

        return new KpiBreakdownDto(
            KpiKey: "Cagr",
            DisplayName: _localizer["Kpi_Cagr_DisplayName"],
            FormulaText: _localizer["Kpi_Cagr_Formula"],
            Description: _localizer["Kpi_Cagr_Description"],
            Groups: cagrGroups);
    }

    /// <summary>
    /// Loads all security transactions for the given security, enforcing ownership via JOIN on Security (Blocker S-1).
    /// </summary>
    /// <param name="securityId">The security id.</param>
    /// <param name="ownerUserId">The owner user id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    private async Task<List<SecurityTransaction>> LoadPostingsAsync(
        Guid securityId, Guid ownerUserId, CancellationToken ct)
    {
        // S-1: Posting has no OwnerUserId → ownership enforced by JOIN through Security.OwnerUserId
        return await _db.Postings
            .AsNoTracking()
            .Join(
                _db.Securities,
                p => p.SecurityId,
                s => (Guid?)s.Id,
                (p, s) => new { p, s })
            .Where(x =>
                x.p.SecurityId == securityId
                && x.s.OwnerUserId == ownerUserId
                && x.p.SecuritySubType != null)
            .OrderBy(x => x.p.BookingDate)
            .ThenBy(x => x.p.Id)
            .Select(x => new SecurityTransaction(
                x.p.Id,
                x.p.BookingDate,
                x.p.SecuritySubType!.Value,
                x.p.Amount,
                x.p.Quantity,
                x.p.GroupId))
            .ToListAsync(ct);
    }

    /// <summary>
    /// Loads the price history for a security in the given date range, enforcing ownership via JOIN.
    /// </summary>
    /// <param name="securityId">The security id.</param>
    /// <param name="ownerUserId">The owner user id.</param>
    /// <param name="from">The from.</param>
    /// <param name="to">The to.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    private async Task<List<(DateTime Date, decimal Close)>> LoadPriceHistoryAsync(
        Guid securityId, Guid ownerUserId, DateTime from, DateTime to, CancellationToken ct)
    {
        var rows = await _db.SecurityPrices
            .AsNoTracking()
            .Join(
                _db.Securities,
                sp => sp.SecurityId,
                s => s.Id,
                (sp, s) => new { sp, s })
            .Where(x =>
                x.sp.SecurityId == securityId
                && x.s.OwnerUserId == ownerUserId
                && x.sp.Date >= from.Date
                && x.sp.Date <= to.Date)
            .OrderBy(x => x.sp.Date)
            .Select(x => new { x.sp.Date, x.sp.Close })
            .ToListAsync(ct);

        return rows.Select(r => (r.Date, r.Close)).ToList();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Computation helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Forward-fills price data to fill gaps (weekends, holidays). Returns only dates with prices.
    /// </summary>
    /// <param name="prices">The prices.</param>
    /// <param name="from">The from.</param>
    /// <param name="to">The to.</param>
    /// <returns>The result.</returns>
    private static List<(DateTime Date, decimal Close)> ForwardFill(
        List<(DateTime Date, decimal Close)> prices, DateTime from, DateTime to)
    {
        if (prices.Count == 0) return prices;

        var result = new List<(DateTime Date, decimal Close)>();
        var priceMap = prices.ToDictionary(p => p.Date.Date, p => p.Close);
        decimal? lastPrice = null;

        for (var date = from.Date; date <= to.Date; date = date.AddDays(1))
        {
            if (priceMap.TryGetValue(date, out var price))
            {
                lastPrice = price;
            }
            if (lastPrice.HasValue)
            {
                result.Add((date, lastPrice.Value));
            }
        }

        return result;
    }

    /// <summary>
    /// Builds IRR cashflows from transactions. Buys are negative, sells and dividends (net of taxes) are positive.
    /// The current market value is added as the terminal inflow on <paramref name="terminalDate"/>.
    /// </summary>
    /// <param name="transactions">The transactions.</param>
    /// <param name="currentMarketValue">The current market value.</param>
    /// <param name="terminalDate">The terminal date.</param>
    /// <returns>The result.</returns>
    private static IReadOnlyList<CashflowPoint> BuildIrrCashflows(
        IReadOnlyList<SecurityTransaction> transactions,
        decimal currentMarketValue,
        DateTime terminalDate)
    {
        var cashflows = new List<CashflowPoint>();

        foreach (var tx in transactions)
        {
            // Buys: amount is negative (outflow) by domain convention
            // Sells, Dividends: amount is positive (inflow)
            // Taxes, Fees: negative (outflow)
            cashflows.Add(new CashflowPoint(tx.Date, tx.Amount));
        }

        // Terminal inflow = current market value of remaining shares
        if (currentMarketValue > 0m)
        {
            cashflows.Add(new CashflowPoint(terminalDate, currentMarketValue));
        }

        return cashflows.AsReadOnly();
    }

    /// <summary>
    /// Builds the chronologically-sorted cashflow timeline items for the IRR info-panel breakdown.
    /// Each buy (plus its linked fees) becomes one negative item; each dividend minus linked taxes
    /// becomes one positive item; each sell becomes one positive item.
    /// The current market value is appended last as the terminal positive cashflow.
    /// When <paramref name="irrValue"/> is provided the <see cref="KpiBreakdownItem.Amount"/> of each
    /// item is the discounted present value (Cashflow / (1+r)^t); the original cashflow, the years
    /// since t₀, and the discount factor are stored in the dedicated fields of
    /// <see cref="KpiBreakdownItem"/>.
    /// </summary>
    /// <param name="buys">Buy transactions.</param>
    /// <param name="dividends">Dividend transactions.</param>
    /// <param name="sells">Sell transactions.</param>
    /// <param name="taxes">Tax transactions.</param>
    /// <param name="fees">Fee transactions.</param>
    /// <param name="feesByGroupId">Lookup of fees keyed by GroupId (for linking to buys).</param>
    /// <param name="taxesByGroupId">Lookup of taxes keyed by GroupId (for linking to dividends).</param>
    /// <param name="currentMarketValue">Current market value of the holding (terminal inflow).</param>
    /// <param name="irrValue">Computed IRR rate used to discount each cashflow. Null → no discounting.</param>
    /// <returns>The result.</returns>
    private List<KpiBreakdownItem> BuildIrrTimelineItems(
        List<SecurityTransaction> buys,
        List<SecurityTransaction> dividends,
        List<SecurityTransaction> sells,
        List<SecurityTransaction> taxes,
        List<SecurityTransaction> fees,
        ILookup<Guid, SecurityTransaction> feesByGroupId,
        ILookup<Guid, SecurityTransaction> taxesByGroupId,
        decimal currentMarketValue,
        decimal? irrValue)
    {
        var items = new List<KpiBreakdownItem>();

        // Buys: negative cashflow = -(Kaufbetrag + verknüpfte Gebühren)
        foreach (var buy in buys)
        {
            decimal linkedFeeAmt = buy.GroupId != Guid.Empty
                ? feesByGroupId[buy.GroupId].Sum(f => f.Amount) // negative domain values
                : 0m;
            decimal cashflowAmt = buy.Amount + linkedFeeAmt; // both negative → more negative
            decimal absPrice = buy.Quantity is > 0m ? Math.Abs(buy.Amount) / buy.Quantity.Value : 0m;
            string label = buy.Quantity.HasValue
                ? _localizer.Format("Item_Buy", buy.Quantity.Value.ToString("G6"), absPrice.ToString("N2"))
                : _localizer["Item_BuySimple"];
            items.Add(new KpiBreakdownItem(buy.Date, cashflowAmt, label));
        }

        // Dividends: positive cashflow = Bruttodividende − verknüpfte Steuern (netto)
        foreach (var div in dividends)
        {
            decimal linkedTaxAmt = div.GroupId != Guid.Empty
                ? taxesByGroupId[div.GroupId].Sum(t => t.Amount) // negative domain values
                : 0m;
            decimal netDivCashflow = div.Amount + linkedTaxAmt;
            items.Add(new KpiBreakdownItem(div.Date, netDivCashflow, _localizer["Item_Dividend_Net"]));
        }

        // Sells: positive cashflow (proceeds)
        foreach (var sell in sells)
        {
            decimal absPrice = sell.Quantity is > 0m ? sell.Amount / sell.Quantity.Value : 0m;
            string label = sell.Quantity.HasValue
                ? _localizer.Format("Item_SellWithDetails", sell.Quantity.Value.ToString("G6"), absPrice.ToString("N2"))
                : _localizer["Item_Sell"];
            items.Add(new KpiBreakdownItem(sell.Date, sell.Amount, label));
        }

        // Sort chronologically
        items.Sort((a, b) => DateTime.Compare(a.Date, b.Date));

        // Terminal inflow: aktueller Marktwert (fiktiver Abschluss)
        items.Add(new KpiBreakdownItem(
            DateTime.Today,
            currentMarketValue,
            _localizer["Item_MarketValueTerminal"]));

        // ── Discounting ───────────────────────────────────────────────────────
        // When IRR is available replace each item's Amount with its present value and populate
        // the YearsSinceT0, DiscountFactor, and OriginalCashflow supplementary fields.
        if (irrValue.HasValue && items.Count > 0)
        {
            double r = (double)irrValue.Value;
            DateTime t0 = items[0].Date;

            var discounted = new List<KpiBreakdownItem>(items.Count);
            foreach (var item in items)
            {
                double tYears = (item.Date - t0).TotalDays / 365.25;
                double dfDouble = 1.0 / Math.Pow(1.0 + r, tYears);
                decimal df = (decimal)dfDouble;
                decimal pv = item.Amount * df;
                discounted.Add(item with
                {
                    Amount = pv,
                    YearsSinceT0 = (decimal)tYears,
                    DiscountFactor = df,
                    OriginalCashflow = item.Amount,
                });
            }
            return discounted;
        }

        return items;
    }


    /// <summary>
    /// Builds the time-weighted return periods from transactions and filled prices.
    /// Each period spans between consecutive cashflow events.
    /// </summary>
    /// <param name="transactions">The transactions.</param>
    /// <param name="filledPrices">The filled prices.</param>
    /// <returns>The result.</returns>
    private static IReadOnlyList<TwrPeriodInput> BuildTwrPeriods(
        IReadOnlyList<SecurityTransaction> transactions,
        IReadOnlyList<(DateTime Date, decimal Close)> filledPrices)
    {
        if (filledPrices.Count == 0 || transactions.Count == 0)
            return Array.Empty<TwrPeriodInput>();

        var priceMap = filledPrices.ToDictionary(p => p.Date.Date, p => p.Close);

        // Cashflow event dates (Buy, Sell only – dividends are not portfolio cashflows for TWR)
        var cashflowDates = transactions
            .Where(t => t.Type == SecurityPostingSubType.Buy || t.Type == SecurityPostingSubType.Sell)
            .Select(t => t.Date.Date)
            .Distinct()
            .OrderBy(d => d)
            .ToList();

        if (cashflowDates.Count == 0) return Array.Empty<TwrPeriodInput>();

        var periods = new List<TwrPeriodInput>();
        var allDates = filledPrices.Select(p => p.Date.Date).ToList();

        // Build a running snapshot of shares held at any date
        DateTime? lastEventDate = null;

        foreach (var eventDate in cashflowDates)
        {
            if (lastEventDate == null)
            {
                lastEventDate = eventDate;
                continue;
            }

            var start = lastEventDate.Value;
            var end = eventDate;

            if (!priceMap.TryGetValue(start, out var startPrice) ||
                !priceMap.TryGetValue(end, out var endPrice))
            {
                lastEventDate = eventDate;
                continue;
            }

            decimal sharesAtStart = ComputeSharesHeldOnDate(transactions, start);
            decimal sharesAtEnd = ComputeSharesHeldOnDate(transactions, start); // before end-of-day cashflow

            decimal startValue = sharesAtStart * startPrice;
            decimal endValue = sharesAtEnd * endPrice;

            // External cashflow on end date: net buy/sell amounts
            decimal externalCashflow = transactions
                .Where(t => t.Date.Date == end
                         && (t.Type == SecurityPostingSubType.Buy || t.Type == SecurityPostingSubType.Sell))
                .Sum(t => t.Amount);

            periods.Add(new TwrPeriodInput(start, end, startValue, endValue, externalCashflow));
            lastEventDate = eventDate;
        }

        // Final period: from last event to today
        if (lastEventDate.HasValue && allDates.Count > 0)
        {
            var finalStart = lastEventDate.Value;
            var finalEnd = allDates[^1];

            if (priceMap.TryGetValue(finalStart, out var fStartPrice) &&
                priceMap.TryGetValue(finalEnd, out var fEndPrice) &&
                finalStart < finalEnd)
            {
                decimal shares = ComputeSharesHeldOnDate(transactions, finalEnd);
                decimal startValue = shares * fStartPrice;
                decimal endValue = shares * fEndPrice;

                periods.Add(new TwrPeriodInput(finalStart, finalEnd, startValue, endValue, 0m));
            }
        }

        return periods.AsReadOnly();
    }

    /// <summary>
    /// Builds sparkline data points from transactions and forward-filled prices.
    /// </summary>
    /// <param name="transactions">The transactions.</param>
    /// <param name="filledPrices">The filled prices.</param>
    /// <returns>The result.</returns>
    private static IReadOnlyList<SparklinePoint> BuildSparklinePoints(
        IReadOnlyList<SecurityTransaction> transactions,
        IReadOnlyList<(DateTime Date, decimal Close)> filledPrices)
    {
        var points = new List<SparklinePoint>(filledPrices.Count);

        foreach (var (date, close) in filledPrices)
        {
            decimal shares = ComputeSharesHeldOnDate(transactions, date);
            decimal marketValue = shares * close;
            decimal investedCapital = ComputeInvestedCapitalOnDate(transactions, date);
            points.Add(new SparklinePoint(date, marketValue, investedCapital));
        }

        return points.AsReadOnly();
    }

    /// <summary>
    /// Builds a list of portfolio market values (for volatility / max-drawdown calculation).
    /// </summary>
    /// <param name="transactions">The transactions.</param>
    /// <param name="filledPrices">The filled prices.</param>
    /// <returns>The result.</returns>
    private static List<decimal> BuildPortfolioValueSeries(
        IReadOnlyList<SecurityTransaction> transactions,
        IReadOnlyList<(DateTime Date, decimal Close)> filledPrices)
    {
        var result = new List<decimal>(filledPrices.Count);
        foreach (var (date, close) in filledPrices)
        {
            decimal shares = ComputeSharesHeldOnDate(transactions, date);
            result.Add(shares * close);
        }
        return result;
    }

    /// <summary>
    /// Computes shares held on or before a given date from transaction history.
    /// </summary>
    /// <param name="transactions">The transactions.</param>
    /// <param name="date">The date.</param>
    /// <returns>The result.</returns>
    private static decimal ComputeSharesHeldOnDate(
        IReadOnlyList<SecurityTransaction> transactions, DateTime date)
    {
        return SecurityValuationHelper.SharesHeldOnDate(transactions, date);
    }

    /// <summary>
    /// Computes invested capital (cost basis of remaining shares) on a given date using a simple running sum.
    /// For a precise FIFO basis, use FifoCostBasisCalculator on filtered transactions.
    /// </summary>
    /// <param name="transactions">The transactions.</param>
    /// <param name="date">The date.</param>
    /// <returns>The result.</returns>
    private static decimal ComputeInvestedCapitalOnDate(
        IReadOnlyList<SecurityTransaction> transactions, DateTime date)
    {
        decimal capital = 0m;
        foreach (var tx in transactions)
        {
            if (tx.Date.Date > date.Date) break;
            if (tx.Type == SecurityPostingSubType.Buy)
                capital += Math.Abs(tx.Amount);
            else if (tx.Type == SecurityPostingSubType.Sell)
            {
                // Reduce proportionally – approximation (full FIFO would need lot tracking per day)
                capital -= tx.Amount; // sell amount is positive, so subtract
            }
        }
        return Math.Max(0m, capital);
    }

    /// <summary>
    /// Returns the portfolio market value on a given date by interpolating from the filled price series.
    /// </summary>
    /// <param name="transactions">The transactions.</param>
    /// <param name="filledPrices">The filled prices.</param>
    /// <param name="date">The date.</param>
    /// <returns>The result.</returns>
    private static decimal GetPortfolioValueOnDate(
        IReadOnlyList<SecurityTransaction> transactions,
        IReadOnlyList<(DateTime Date, decimal Close)> filledPrices,
        DateTime date)
    {
        decimal close = SecurityValuationHelper.LatestPriceOnOrBefore(filledPrices, date);
        decimal shares = ComputeSharesHeldOnDate(transactions, date);
        return shares * close;
    }

    /// <summary>
    /// Builds annual dividend summary list.
    /// </summary>
    /// <param name="transactions">The transactions.</param>
    /// <param name="firstYear">The first year.</param>
    /// <param name="lastYear">The last year.</param>
    /// <returns>The result.</returns>
    private static IReadOnlyList<AnnualDividendPoint> BuildAnnualDividends(
        IReadOnlyList<SecurityTransaction> transactions, int firstYear, int lastYear)
    {
        decimal cumulativeNet = 0m;
        var result = new List<AnnualDividendPoint>();

        for (int year = firstYear; year <= lastYear; year++)
        {
            decimal gross = transactions
                .Where(t => t.Type == SecurityPostingSubType.Dividend && t.Date.Year == year)
                .Sum(t => t.Amount);

            decimal taxes = transactions
                .Where(t => t.Type == SecurityPostingSubType.Tax && t.Date.Year == year)
                .Sum(t => Math.Abs(t.Amount));

            decimal net = gross - taxes;
            cumulativeNet += net;

            result.Add(new AnnualDividendPoint(year, gross, net, cumulativeNet));
        }

        return result.AsReadOnly();
    }

    /// <summary>
    /// Merges simulated and real price series so that the chart always starts from the first
    /// buy transaction. Simulated prices are used only for dates strictly before the first real
    /// price entry; real prices take precedence for all overlapping or later dates.
    /// </summary>
    /// <param name="simulated">Prices derived from transaction implied prices.</param>
    /// <param name="real">Actual price history loaded from the database.</param>
    /// <returns>
    /// A merged, chronologically ordered list: simulated entries up to (but not including) the
    /// first real price date, followed by all real price entries. When no real prices exist, the
    /// full simulated series is returned. When simulated is empty, the real series is returned.
    /// </returns>
    private static List<(DateTime Date, decimal Close)> MergeSimulatedAndRealPrices(
        List<(DateTime Date, decimal Close)> simulated,
        List<(DateTime Date, decimal Close)> real)
    {
        if (simulated.Count == 0) return real;
        if (real.Count == 0) return simulated;

        // Only keep simulated entries that predate the first real price so real data wins
        var firstRealDate = real[0].Date.Date;
        var simBeforeReal = simulated.Where(p => p.Date.Date < firstRealDate).ToList();
        return [.. simBeforeReal, .. real];
    }

    /// <summary>
    /// Simulates a daily price series from buy/sell transaction implied prices when no real
    /// price history is available. The implied unit price for each transaction is
    /// <c>|Amount| / |Quantity|</c>. Between consecutive anchor points the price is linearly
    /// interpolated; the series covers exactly the period from the first to the last buy/sell
    /// transaction date.
    /// </summary>
    /// <param name="transactions">Chronologically ordered security transactions.</param>
    /// <returns>
    /// List of <c>(Date, SimulatedClose)</c> tuples. Returns an empty list when no buy/sell
    /// transactions with both a non-zero quantity and a non-zero amount exist.
    /// </returns>
    private static List<(DateTime Date, decimal Close)> SimulatePricesFromTransactions(
        IReadOnlyList<SecurityTransaction> transactions)
    {
        var anchors = transactions
            .Where(t => (t.Type == SecurityPostingSubType.Buy || t.Type == SecurityPostingSubType.Sell)
                     && t.Quantity.HasValue && t.Quantity.Value != 0m && t.Amount != 0m)
            .OrderBy(t => t.Date)
            .Select(t => (Date: t.Date.Date, Price: Math.Abs(t.Amount) / Math.Abs(t.Quantity!.Value)))
            .ToList();

        if (anchors.Count == 0) return [];

        var result = new List<(DateTime Date, decimal Close)>();

        for (int i = 0; i < anchors.Count - 1; i++)
        {
            var (startDate, startPrice) = anchors[i];
            var (endDate, endPrice) = anchors[i + 1];
            int totalDays = (int)(endDate - startDate).TotalDays;

            for (int d = 0; d < totalDays; d++)
            {
                decimal fraction = totalDays > 0 ? (decimal)d / totalDays : 0m;
                decimal price = startPrice + fraction * (endPrice - startPrice);
                result.Add((startDate.AddDays(d), price));
            }
        }

        // Add the final anchor point
        result.Add(anchors[^1]);

        return result;
    }

    /// <summary>
    /// Converts a <see cref="ChartTimeRange"/> to an absolute from-date.
    /// </summary>
    /// <param name="timeRange">The time range.</param>
    /// <returns>The result.</returns>
    private static DateTime GetFromDateForTimeRange(ChartTimeRange timeRange)
    {
        var today = DateTime.Today;
        return timeRange switch
        {
            ChartTimeRange.OneMonth => today.AddMonths(-1),
            ChartTimeRange.ThreeMonths => today.AddMonths(-3),
            ChartTimeRange.SixMonths => today.AddMonths(-6),
            ChartTimeRange.OneYear => today.AddYears(-1),
            ChartTimeRange.ThreeYears => today.AddYears(-3),
            ChartTimeRange.All => DateTime.MinValue,
            _ => today.AddYears(-1)
        };
    }
}
