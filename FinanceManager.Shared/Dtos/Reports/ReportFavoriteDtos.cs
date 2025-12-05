namespace FinanceManager.Shared.Dtos.Reports;

/// <summary>
/// DTO representing a saved user report favorite.
/// </summary>
public sealed record ReportFavoriteDto(
    Guid Id,
    string Name,
    PostingKind PostingKind,
    bool IncludeCategory,
    ReportInterval Interval,
    int Take,
    bool ComparePrevious,
    bool CompareYear,
    bool ShowChart,
    bool Expandable,
    DateTime CreatedUtc,
    DateTime? ModifiedUtc,
    IReadOnlyCollection<PostingKind> PostingKinds,
    ReportFavoriteFiltersDto? Filters,
    bool UseValutaDate
);
