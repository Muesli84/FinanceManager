namespace FinanceManager.Application.Securities.ReturnAnalysis;

/// <summary>
/// Orchestrates return analysis for a single security. All methods are user-scoped.
/// Results are cached for 1 hour (TTL). Cache is invalidated on new postings or price data.
/// </summary>
public interface IReturnAnalysisService
{
    /// <summary>
    /// Returns the compact return summary for the widget on the security detail page (FR-1).
    /// Cached for 1 hour.
    /// Returns null when the security does not exist or is not owned by the user.
    /// </summary>
    /// <param name="securityId">Identifier of the security.</param>
    /// <param name="ownerUserId">Identifier of the owning user.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Return summary DTO, or null when not found.</returns>
    Task<ReturnSummaryDto?> GetReturnSummaryAsync(Guid securityId, Guid ownerUserId, CancellationToken ct);

    /// <summary>
    /// Returns sparkline data for the mini-chart (FR-1.1). Cached separately.
    /// Returns null when fewer than 30 price data points.
    /// </summary>
    /// <param name="securityId">Identifier of the security.</param>
    /// <param name="ownerUserId">Identifier of the owning user.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Sparkline data DTO, or null when insufficient price data.</returns>
    Task<SparklineDataDto?> GetSparklineDataAsync(Guid securityId, Guid ownerUserId, CancellationToken ct);

    /// <summary>
    /// Returns detailed return metrics for the Kennzahlen tab (FR-2.1).
    /// Returns null when the security does not exist or is not owned by the user.
    /// </summary>
    /// <param name="securityId">Identifier of the security.</param>
    /// <param name="ownerUserId">Identifier of the owning user.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Detailed metrics DTO, or null when not found.</returns>
    Task<DetailedReturnMetricsDto?> GetDetailedMetricsAsync(Guid securityId, Guid ownerUserId, CancellationToken ct);

    /// <summary>
    /// Returns periodic returns (annual + monthly + dividends) for the Zeitliche Entwicklung tab (FR-2.2, FR-2.5).
    /// </summary>
    /// <param name="securityId">Identifier of the security.</param>
    /// <param name="ownerUserId">Identifier of the owning user.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Periodic returns DTO, or null when the security is not found.</returns>
    Task<PeriodicReturnsDto?> GetPeriodicReturnsAsync(Guid securityId, Guid ownerUserId, CancellationToken ct);

    /// <summary>
    /// Returns cashflow timeline for the Cashflows tab (FR-2.3, FR-2.6).
    /// </summary>
    /// <param name="securityId">Identifier of the security.</param>
    /// <param name="ownerUserId">Identifier of the owning user.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Cashflow timeline DTO, or null when the security is not found.</returns>
    Task<CashflowTimelineDto?> GetCashflowTimelineAsync(Guid securityId, Guid ownerUserId, CancellationToken ct);

    /// <summary>
    /// Returns performance chart data for the Übersicht tab (FR-2.4).
    /// </summary>
    /// <param name="securityId">Identifier of the security.</param>
    /// <param name="ownerUserId">Identifier of the owning user.</param>
    /// <param name="timeRange">Selected time range for the chart.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Performance chart data DTO, or null when the security is not found.</returns>
    Task<PerformanceChartDataDto?> GetPerformanceChartDataAsync(Guid securityId, Guid ownerUserId, ChartTimeRange timeRange, CancellationToken ct);

    /// <summary>
    /// Returns benchmark comparison data (FR-7).
    /// Returns null when no benchmark is configured for the user or benchmark has insufficient price data.
    /// Validates that the benchmark security is owned by the same user (S-3).
    /// </summary>
    /// <param name="securityId">Identifier of the security.</param>
    /// <param name="ownerUserId">Identifier of the owning user.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Benchmark comparison DTO, or null when no benchmark is configured or data is insufficient.</returns>
    Task<BenchmarkComparisonDto?> GetBenchmarkComparisonAsync(Guid securityId, Guid ownerUserId, CancellationToken ct);

    /// <summary>
    /// Returns the return analysis settings for the current user (benchmark config, Sharpe Ratio opt-in).
    /// </summary>
    /// <param name="ownerUserId">Identifier of the user whose settings are requested.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Return analysis settings DTO, or null when the user is not found.</returns>
    Task<ReturnAnalysisSettingsDto?> GetUserSettingsAsync(Guid ownerUserId, CancellationToken ct);

    /// <summary>
    /// Updates return analysis settings for the user.
    /// Validates that benchmarkSecurityId (if set) is owned by ownerUserId.
    /// </summary>
    /// <param name="ownerUserId">Identifier of the user whose settings are updated.</param>
    /// <param name="benchmarkSecurityId">Optional benchmark security id. Null clears the benchmark.</param>
    /// <param name="showSharpeRatio">Whether to enable Sharpe Ratio display.</param>
    /// <param name="riskFreeRate">Risk-free rate for Sharpe Ratio calculation. Must be >= 0.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes when settings have been persisted.</returns>
    Task UpdateUserSettingsAsync(Guid ownerUserId, Guid? benchmarkSecurityId, bool showSharpeRatio, decimal riskFreeRate, CancellationToken ct);

    /// <summary>
    /// Returns the KPI formula and cashflow breakdown for all widget KPIs (FR-1 side panel).
    /// Used by the info panel to display the formula, explanation, and individual postings grouped by formula element.
    /// Cached for 1 hour together with the summary.
    /// Returns null when the security does not exist, is not owned by the user, or has no transactions.
    /// </summary>
    /// <param name="securityId">Identifier of the security.</param>
    /// <param name="ownerUserId">Identifier of the owning user.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of per-KPI breakdown DTOs, or null when not found / no data.</returns>
    Task<IReadOnlyList<KpiBreakdownDto>?> GetKpiBreakdownsAsync(Guid securityId, Guid ownerUserId, CancellationToken ct);

    /// <summary>
    /// Invalidates all cached results for the given security and user.
    /// Called when new postings or price data arrive.
    /// </summary>
    /// <param name="securityId">Identifier of the security whose cache is invalidated.</param>
    /// <param name="ownerUserId">Identifier of the owning user.</param>
    /// <returns>A task that completes when the cache has been invalidated.</returns>
    Task InvalidateCacheAsync(Guid securityId, Guid ownerUserId);

    /// <summary>
    /// Invalidates all cached return analysis results for the given user across all securities.
    /// Used by the API endpoint to reset the full return analysis cache for a user at once.
    /// </summary>
    /// <param name="ownerUserId">Identifier of the user whose entire return analysis cache should be cleared.</param>
    /// <returns>A task that completes when the cache has been invalidated.</returns>
    Task InvalidateAllUserCachesAsync(Guid ownerUserId);
}

/// <summary>Return analysis user settings DTO.</summary>
/// <param name="BenchmarkSecurityId">Optional benchmark security id.</param>
/// <param name="BenchmarkSecurityName">Display name of the benchmark security, or null.</param>
/// <param name="ShowSharpeRatio">Whether Sharpe Ratio is enabled.</param>
/// <param name="RiskFreeRate">Risk-free rate for Sharpe Ratio (e.g. 0.04 = 4%).</param>
/// <returns>The result.</returns>
public sealed record ReturnAnalysisSettingsDto(
    Guid? BenchmarkSecurityId,
    string? BenchmarkSecurityName,
    bool ShowSharpeRatio,
    decimal RiskFreeRate
);
