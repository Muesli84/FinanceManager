using System.Text.Json.Serialization;

namespace FinanceManager.Shared.Dtos.Reports;

/// <summary>
/// DTO representing a saved user report favorite.
/// </summary>
/// <returns>The result.</returns>
[method: JsonConstructor]
public sealed record ReportFavoriteDto(
    Guid Id,
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
    DateTime CreatedUtc,
    DateTime? ModifiedUtc,
    IReadOnlyCollection<PostingKind> PostingKinds,
    ReportFavoriteFiltersDto? Filters,
    bool UseValutaDate
)
{
    /// <summary>
    /// Compatibility constructor for callers that do not provide projection settings.
    /// </summary>
    public ReportFavoriteDto(
        Guid id,
        string name,
        PostingKind postingKind,
        bool includeCategory,
        ReportInterval interval,
        int take,
        bool comparePrevious,
        bool compareYear,
        bool showChart,
        bool expandable,
        DateTime createdUtc,
        DateTime? modifiedUtc,
        IReadOnlyCollection<PostingKind> postingKinds,
        ReportFavoriteFiltersDto? filters,
        bool useValutaDate)
        : this(id, name, postingKind, includeCategory, interval, take, comparePrevious, compareYear, false, showChart, expandable, createdUtc, modifiedUtc, postingKinds, filters, useValutaDate)
    {
    }
}
