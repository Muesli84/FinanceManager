namespace FinanceManager.Infrastructure.Securities.ReturnAnalysis;

/// <summary>Cache key helper for return analysis cache entries.</summary>
/// <remarks>
/// Key format: <c>ra:{type}:{securityId}:{userId}</c> (or with optional extra suffix).
/// This layout allows prefix-based invalidation of all entries for a security/user pair
/// via prefix <c>ra:{securityId}:{userId}:</c>.
/// </remarks>
internal static class ReturnAnalysisCacheKeys
{
    private const string Version = "v2";

    /// <summary>Cache key for a return summary entry.</summary>
    /// <param name="securityId">The security id.</param>
    /// <param name="userId">The user id.</param>
    /// <returns>The result.</returns>
    internal static string Summary(Guid securityId, Guid userId)
        => $"ra:summary:{securityId}:{userId}";

    /// <summary>Cache key for sparkline data.</summary>
    /// <param name="securityId">The security id.</param>
    /// <param name="userId">The user id.</param>
    /// <returns>The result.</returns>
    internal static string Sparkline(Guid securityId, Guid userId)
        => $"ra:sparkline:{Version}:{securityId}:{userId}";

    /// <summary>Cache key for detailed metrics.</summary>
    /// <param name="securityId">The security id.</param>
    /// <param name="userId">The user id.</param>
    /// <returns>The result.</returns>
    internal static string Metrics(Guid securityId, Guid userId)
        => $"ra:metrics:{securityId}:{userId}";

    /// <summary>Cache key for periodic returns.</summary>
    /// <param name="securityId">The security id.</param>
    /// <param name="userId">The user id.</param>
    /// <returns>The result.</returns>
    internal static string Periodic(Guid securityId, Guid userId)
        => $"ra:periodic:{Version}:{securityId}:{userId}";

    /// <summary>Cache key for cashflow timeline.</summary>
    /// <param name="securityId">The security id.</param>
    /// <param name="userId">The user id.</param>
    /// <returns>The result.</returns>
    internal static string Cashflow(Guid securityId, Guid userId)
        => $"ra:cashflow:{securityId}:{userId}";

    /// <summary>Cache key for performance chart data with a given time range.</summary>
    /// <param name="securityId">The security id.</param>
    /// <param name="userId">The user id.</param>
    /// <param name="timeRange">The time range.</param>
    /// <returns>The result.</returns>
    internal static string Chart(Guid securityId, Guid userId, string timeRange)
        => $"ra:chart:{Version}:{securityId}:{userId}:{timeRange}";

    /// <summary>Cache key for benchmark comparison data.</summary>
    /// <param name="securityId">The security id.</param>
    /// <param name="userId">The user id.</param>
    /// <returns>The result.</returns>
    internal static string Benchmark(Guid securityId, Guid userId)
        => $"ra:benchmark:{securityId}:{userId}";

    /// <summary>Cache key for KPI breakdown data (info panel).</summary>
    /// <param name="securityId">The security id.</param>
    /// <param name="userId">The user id.</param>
    /// <returns>The result.</returns>
    internal static string KpiBreakdown(Guid securityId, Guid userId)
        => $"ra:kpibreakdown:{securityId}:{userId}";

    /// <summary>
    /// Returns the prefix used to invalidate all cache entries for a specific security/user pair.
    /// Matches keys of format <c>ra:*:{securityId}:{userId}*</c> by containing both IDs.
    /// </summary>
    /// <param name="securityId">The security id.</param>
    /// <param name="userId">The user id.</param>
    /// <remarks>
    /// Because all keys contain <c>{securityId}:{userId}</c> as a suffix after the type,
    /// and the <see cref="MemoryReturnAnalysisCache"/> performs a substring search,
    /// we use both IDs as the invalidation token.
    /// </remarks>
    /// <returns>The result.</returns>
    internal static string SecurityUserToken(Guid securityId, Guid userId)
        => $"{securityId}:{userId}";
}
