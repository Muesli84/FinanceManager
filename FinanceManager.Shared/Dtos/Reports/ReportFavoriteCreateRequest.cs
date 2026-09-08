using System.Text.Json.Serialization;

namespace FinanceManager.Shared.Dtos.Reports;

/// <summary>
/// Request to create a report favorite.
/// Represents the client payload for creating a saved report configuration (favorite).
/// </summary>
/// <param name="Name">Display name of the favorite.</param>
/// <param name="PostingKind">Primary posting kind to include in the report (bank, contact, savings plan, security).</param>
/// <param name="IncludeCategory">When true, include category grouping in the aggregation.</param>
/// <param name="Interval">Reporting interval (e.g. Month, Year).</param>
/// <param name="Take">Number of results / buckets to take for the report (paging/limit for aggregation).</param>
/// <param name="ComparePrevious">When true, include comparison to the previous period.</param>
/// <param name="CompareYear">When true, include year-over-year comparison.</param>
/// <param name="CompareProjection">When true, include dividend projection.</param>
/// <param name="ShowChart">When true, the UI should show a chart for this favorite.</param>
/// <param name="Expandable">When true, report rows can be expanded in the UI.</param>
/// <param name="PostingKinds">Optional collection of posting kinds to include (overrides primary PostingKind when provided).</param>
/// <param name="Filters">Optional additional filters to apply to the report (categories, contacts, accounts, etc.).</param>
/// <param name="UseValutaDate">When true, use valuta/booking date instead of value date when aggregating.</param>
/// <returns>The result.</returns>
[method: JsonConstructor]
public sealed record ReportFavoriteCreateRequest(
    string Name,
    PostingKind PostingKind,
    bool IncludeCategory,
    ReportInterval Interval,
    int Take,
    bool ComparePrevious,
    bool CompareYear,
    bool CompareProjection,
    bool ShowChart,
    bool Expandable,
    IReadOnlyCollection<PostingKind>? PostingKinds = null,
    ReportFavoriteFiltersDto? Filters = null,
    bool UseValutaDate = false
)
{
    /// <summary>
    /// Convenience overload that creates a report favorite using a default <c>Take</c> value of 24.
    /// </summary>
    /// <param name="name">Display name of the favorite.</param>
    /// <param name="postingKind">Primary posting kind to include in the report.</param>
    /// <param name="includeCategory">When true, include category grouping in the aggregation.</param>
    /// <param name="interval">Reporting interval.</param>
    /// <param name="comparePrevious">When true, include comparison to the previous period.</param>
    /// <param name="compareYear">When true, include year-over-year comparison.</param>
    /// <param name="compareProjection">When true, include dividend projection.</param>
    /// <param name="showChart">When true, the UI should show a chart for this favorite.</param>
    /// <param name="expandable">When true, report rows can be expanded in the UI.</param>
    /// <param name="postingKinds">Optional collection of posting kinds to include.</param>
    /// <param name="filters">Optional additional filters to apply to the report.</param>
    /// <param name="UseValutaDate">When true, use valuta/booking date instead of value date when aggregating.</param>
    public ReportFavoriteCreateRequest(string name, PostingKind postingKind, bool includeCategory, ReportInterval interval,
        bool comparePrevious, bool compareYear, bool compareProjection, bool showChart, bool expandable,
        IReadOnlyCollection<PostingKind>? postingKinds = null, ReportFavoriteFiltersDto? filters = null, bool UseValutaDate = false)
        : this(name, postingKind, includeCategory, interval, 24, comparePrevious, compareYear, compareProjection, showChart, expandable, postingKinds, filters, UseValutaDate) { }

    /// <summary>
    /// Compatibility overload that disables projection when the caller does not provide it.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <param name="postingKind">The posting kind.</param>
    /// <param name="includeCategory">The include category.</param>
    /// <param name="interval">The interval.</param>
    /// <param name="comparePrevious">The compare previous.</param>
    /// <param name="compareYear">The compare year.</param>
    /// <param name="showChart">The show chart.</param>
    /// <param name="expandable">The expandable.</param>
    /// <param name="postingKinds">The posting kinds.</param>
    /// <param name="filters">The filters.</param>
    /// <param name="UseValutaDate">The use valuta date.</param>
    public ReportFavoriteCreateRequest(string name, PostingKind postingKind, bool includeCategory, ReportInterval interval,
        bool comparePrevious, bool compareYear, bool showChart, bool expandable,
        IReadOnlyCollection<PostingKind>? postingKinds = null, ReportFavoriteFiltersDto? filters = null, bool UseValutaDate = false)
        : this(name, postingKind, includeCategory, interval, 24, comparePrevious, compareYear, false, showChart, expandable, postingKinds, filters, UseValutaDate) { }
}
