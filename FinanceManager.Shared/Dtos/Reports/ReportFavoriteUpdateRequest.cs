namespace FinanceManager.Shared.Dtos.Reports;

/// <summary>
/// Request to update a report favorite.
/// </summary>
public sealed record ReportFavoriteUpdateRequest(
    string Name,
    PostingKind PostingKind,
    bool IncludeCategory,
    ReportInterval Interval,
    int Take,
    bool ComparePrevious,
    bool CompareYear,
    bool ShowChart,
    bool Expandable,
    IReadOnlyCollection<PostingKind>? PostingKinds = null,
    ReportFavoriteFiltersDto? Filters = null,
    bool UseValutaDate = false
)
{
    public ReportFavoriteUpdateRequest(string name, PostingKind postingKind, bool includeCategory, ReportInterval interval,
        bool comparePrevious, bool compareYear, bool showChart, bool expandable,
        IReadOnlyCollection<PostingKind>? postingKinds = null, ReportFavoriteFiltersDto? filters = null, bool UseValutaDate = false)
        : this(name, postingKind, includeCategory, interval, 24, comparePrevious, compareYear, showChart, expandable, postingKinds, filters, UseValutaDate) { }
}
