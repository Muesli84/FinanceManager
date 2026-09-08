using FinanceManager.Domain.Postings; // AggregatePeriod

namespace FinanceManager.Application.Reports;

/// <summary>
/// Provides aggregated posting time series (already pre-aggregated in PostingAggregates) for different posting kinds / entities.
/// </summary>
public interface IPostingTimeSeriesService
{
    /// <summary>
    /// Returns an ordered (ascending by PeriodStart) list of aggregate points or null when the entity does not belong to the user.
    /// Optional maxYearsBack (1..10) limits the earliest PeriodStart considered.
    /// </summary>
    /// <param name="ownerUserId">The owner user id.</param>
    /// <param name="kind">The kind.</param>
    /// <param name="entityId">The entity id.</param>
    /// <param name="period">The period.</param>
    /// <param name="take">The take.</param>
    /// <param name="maxYearsBack">The max years back.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
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
    /// Optional maxYearsBack (1..10) limits the earliest PeriodStart considered.
    /// </summary>
    /// <param name="ownerUserId">The owner user id.</param>
    /// <param name="kind">The kind.</param>
    /// <param name="period">The period.</param>
    /// <param name="take">The take.</param>
    /// <param name="maxYearsBack">The max years back.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    Task<IReadOnlyList<AggregatePointDto>> GetAllAsync(
        Guid ownerUserId,
        PostingKind kind,
        AggregatePeriod period,
        int take,
        int? maxYearsBack,
        CancellationToken ct);
}
