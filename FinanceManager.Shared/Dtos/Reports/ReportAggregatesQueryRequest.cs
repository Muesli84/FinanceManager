using System.Text.Json.Serialization;

namespace FinanceManager.Shared.Dtos.Reports;

/// <summary>
/// Request payload to query aggregate reports for a given context.
/// </summary>
/// <param name="PostingKind">The posting kind.</param>
/// <param name="Interval">The interval.</param>
/// <param name="Take">The take.</param>
/// <param name="IncludeCategory">The include category.</param>
/// <param name="ComparePrevious">The compare previous.</param>
/// <param name="CompareYear">The compare year.</param>
/// <param name="CompareProjection">The compare projection.</param>
/// <param name="UseValutaDate">The use valuta date.</param>
/// <param name="PostingKinds">The posting kinds.</param>
/// <param name="AnalysisDate">The analysis date.</param>
/// <param name="Filters">The filters.</param>
/// <returns>The result.</returns>
[method: JsonConstructor]
public sealed record ReportAggregatesQueryRequest(
    PostingKind PostingKind,
    ReportInterval Interval,
    int Take = 24,
    bool IncludeCategory = false,
    bool ComparePrevious = false,
    bool CompareYear = false,
    bool CompareProjection = false,
    bool UseValutaDate = false,
    IReadOnlyCollection<PostingKind>? PostingKinds = null,
    DateTime? AnalysisDate = null,
    ReportAggregatesFiltersRequest? Filters = null
)
{
    /// <summary>
    /// Compatibility constructor for callers that do not provide projection settings.
    /// </summary>
    /// <param name="postingKind">The posting kind.</param>
    /// <param name="interval">The interval.</param>
    /// <param name="take">The take.</param>
    /// <param name="includeCategory">The include category.</param>
    /// <param name="comparePrevious">The compare previous.</param>
    /// <param name="compareYear">The compare year.</param>
    /// <param name="useValutaDate">The use valuta date.</param>
    /// <param name="postingKinds">The posting kinds.</param>
    /// <param name="analysisDate">The analysis date.</param>
    /// <param name="filters">The filters.</param>
    public ReportAggregatesQueryRequest(
        PostingKind postingKind,
        ReportInterval interval,
        int take,
        bool includeCategory,
        bool comparePrevious,
        bool compareYear,
        bool useValutaDate,
        IReadOnlyCollection<PostingKind>? postingKinds,
        DateTime? analysisDate,
        ReportAggregatesFiltersRequest? filters)
        : this(postingKind, interval, take, includeCategory, comparePrevious, compareYear, false, useValutaDate, postingKinds, analysisDate, filters)
    {
    }
}
