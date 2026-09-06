namespace FinanceManager.Application.Securities.ReturnAnalysis;

// ReturnSummaryDto, DetailedReturnMetricsDto, KpiBreakdownDto, KpiFormulaGroup, KpiBreakdownItem,
// PeriodicReturnsDto, AnnualReturnPoint, MonthlyReturnPoint, AnnualDividendPoint,
// CashflowTimelineDto, CashflowEntry, AnnualCashflowSummary,
// PerformanceChartDataDto, BenchmarkComparisonDto, ChartPoint, ChartTimeRange,
// SparklineDataDto, SparklinePoint
// have been moved to FinanceManager.Shared.Dtos.Securities.ReturnAnalysisDtos and are available
// globally via the FinanceManager.Application.GlobalUsings (global using FinanceManager.Shared.Dtos.Securities).

/// <summary>FIFO cost basis calculation result.</summary>
/// <param name="TotalCostBasis">
/// Total cost basis of all remaining lots, including standalone fees (fees not linked to any lot
/// via GroupId). Represents the total invested capital as seen from a FIFO perspective.
/// </param>
/// <param name="RealizedGains">Total realized gains from sells (FIFO).</param>
/// <param name="RemainingLots">Remaining open lots after all sells.</param>
/// <param name="TotalSharesHeld">Total shares currently held.</param>
/// <param name="HasOversellWarning">True when sells exceeded available lots (incomplete data).</param>
/// <param name="OversellWarningMessage">Warning message when HasOversellWarning is true.</param>
/// <param name="StandaloneFeeTotal">
/// Sum of all fee amounts whose GroupId did not match any Buy lot (standalone fees).
/// These are included in <see cref="TotalCostBasis"/> but are tracked separately to allow
/// detailed cost-basis breakdowns in the UI (e.g. "Kaufpreise + Gebühren = Investiertes Kapital").
/// </param>
/// <returns>The result.</returns>
public sealed record FifoCostBasisResult(
    decimal TotalCostBasis,
    decimal RealizedGains,
    IReadOnlyList<FifoLot> RemainingLots,
    decimal TotalSharesHeld,
    bool HasOversellWarning,
    string? OversellWarningMessage,
    decimal StandaloneFeeTotal
);

/// <summary>A single FIFO lot representing a purchase batch.</summary>
/// <param name="PurchaseDate">Date of purchase.</param>
/// <param name="Quantity">Number of shares in this lot.</param>
/// <param name="CostPerUnit">Cost per share for this lot.</param>
/// <returns>The result.</returns>
public sealed record FifoLot(DateTime PurchaseDate, decimal Quantity, decimal CostPerUnit);
