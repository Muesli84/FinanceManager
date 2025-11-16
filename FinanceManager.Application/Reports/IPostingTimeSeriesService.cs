using FinanceManager.Domain; // PostingKind
using FinanceManager.Domain.Postings; // AggregatePeriod

namespace FinanceManager.Application.Reports;

/// <summary>
/// Provides aggregated posting time series (already pre-aggregated in PostingAggregates) for different posting kinds / entities.
/// Implementations return ordered aggregate points (ascending by PeriodStart).
/// </summary>
public interface IPostingTimeSeriesService
{
    /// <summary>
    /// Returns an ordered (ascending by PeriodStart) list of aggregate points for the specified entity owned by <paramref name="ownerUserId"/>.
    /// Returns <c>null</c> when the entity does not belong to the user.
    /// </summary>
    /// <param name="ownerUserId">Owner user identifier used for ownership checks.</param>
    /// <param name="kind">Posting kind (bank/contact/savings plan/security).</param>
    /// <param name="entityId">Identifier of the entity to retrieve aggregates for.</param>
    /// <param name="period">Aggregation period (Month/Quarter/HalfYear/Year).</param>
    /// <param name="take">Maximum number of points to return; service may clamp to supported bounds.</param>
    /// <param name="maxYearsBack">Optional cap (years) to limit how far back points are considered (1..10). Null to disable.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Ordered list of <see cref="AggregatePointDto"/> or <c>null</c> when entity not owned by user.</returns>
    Task<IReadOnlyList<AggregatePointDto>?> GetAsync(
        Guid ownerUserId,
        PostingKind kind,
        Guid entityId,
        AggregatePeriod period,
        int take,
        int? maxYearsBack,
        CancellationToken ct);

    /// <summary>
    /// Returns an ordered (ascending) list of aggregate points across ALL owned entities of the given posting kind.
    /// If the user owns no entities of that kind, an empty list is returned.
    /// </summary>
    /// <param name="ownerUserId">Owner user identifier used for ownership checks.</param>
    /// <param name="kind">Posting kind to aggregate across.</param>
    /// <param name="period">Aggregation period (Month/Quarter/HalfYear/Year).</param>
    /// <param name="take">Maximum number of points to return; service may clamp to supported bounds.</param>
    /// <param name="maxYearsBack">Optional cap (years) to limit how far back points are considered (1..10). Null to disable.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Ordered list of <see cref="AggregatePointDto"/> (may be empty).</returns>
    Task<IReadOnlyList<AggregatePointDto>> GetAllAsync(
        Guid ownerUserId,
        PostingKind kind,
        AggregatePeriod period,
        int take,
        int? maxYearsBack,
        CancellationToken ct);

    /// <summary>
    /// Returns aggregated dividend time series for securities owned by the given user.
    /// Implementations may respect <paramref name="period"/> (typically <see cref="AggregatePeriod.Quarter"/>) and <paramref name="maxYearsBack"/>.
    /// The result is an ordered list of period start / aggregated amount pairs.
    /// </summary>
    /// <param name="ownerUserId">Owner user identifier used for ownership checks.</param>
    /// <param name="period">Aggregation period requested (service may only support quarterly grouping).</param>
    /// <param name="take">Maximum number of points to return; service may clamp to supported bounds.</param>
    /// <param name="maxYearsBack">Optional cap (years) to limit how far back points are considered (1..10). Null to disable.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Ordered list of dividend aggregate points (may be empty).</returns>
    Task<IReadOnlyList<AggregatePointDto>> GetDividendsAsync(
        Guid ownerUserId,
        AggregatePeriod period,
        int take,
        int? maxYearsBack,
        CancellationToken ct);
}

public sealed record AggregatePointDto(DateTime PeriodStart, decimal Amount);
