using FinanceManager.Domain.Reports;

namespace FinanceManager.Application.Reports;

/// <summary>
/// CRUD operations for user scoped report favorites (FA-REP-008).
/// </summary>
public interface IReportFavoriteService
{
    Task<IReadOnlyList<ReportFavoriteDto>> ListAsync(Guid ownerUserId, CancellationToken ct);
    Task<ReportFavoriteDto?> GetAsync(Guid id, Guid ownerUserId, CancellationToken ct);
    Task<ReportFavoriteDto> CreateAsync(Guid ownerUserId, ReportFavoriteCreateRequest request, CancellationToken ct);
    Task<ReportFavoriteDto?> UpdateAsync(Guid id, Guid ownerUserId, ReportFavoriteUpdateRequest request, CancellationToken ct);
    Task<bool> DeleteAsync(Guid id, Guid ownerUserId, CancellationToken ct);
}

public sealed record ReportFavoriteFiltersDto(
    IReadOnlyCollection<Guid>? AccountIds,
    IReadOnlyCollection<Guid>? ContactIds,
    IReadOnlyCollection<Guid>? SavingsPlanIds,
    IReadOnlyCollection<Guid>? SecurityIds,
    IReadOnlyCollection<Guid>? ContactCategoryIds,
    IReadOnlyCollection<Guid>? SavingsPlanCategoryIds,
    IReadOnlyCollection<Guid>? SecurityCategoryIds,
    IReadOnlyCollection<int>? SecuritySubTypes,
    bool? IncludeDividendRelated
);

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
    IReadOnlyCollection<PostingKind> PostingKinds, // multi support (at least one, falls back to single PostingKind if none stored)
    ReportFavoriteFiltersDto? Filters,
    bool UseValutaDate
);

public sealed record ReportFavoriteCreateRequest(
    string Name,
    PostingKind PostingKind,
    bool IncludeCategory,
    ReportInterval Interval,
    int Take,
    bool ComparePrevious,
    bool CompareYear,
    bool ShowChart,
    bool Expandable,
    IReadOnlyCollection<PostingKind>? PostingKinds = null, // optional multi list
    ReportFavoriteFiltersDto? Filters = null,
    bool UseValutaDate = false
)
{
    public ReportFavoriteCreateRequest(string name, PostingKind postingKind, bool includeCategory, ReportInterval interval,
        bool comparePrevious, bool compareYear, bool showChart, bool expandable,
        IReadOnlyCollection<PostingKind>? postingKinds = null, ReportFavoriteFiltersDto? filters = null, bool useValutaDate = false)
        : this(name, postingKind, includeCategory, interval, 24, comparePrevious, compareYear, showChart, expandable, postingKinds, filters, useValutaDate) { }
}

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
    IReadOnlyCollection<PostingKind>? PostingKinds = null, // optional multi list
    ReportFavoriteFiltersDto? Filters = null,
    bool UseValutaDate = false
)
{
    public ReportFavoriteUpdateRequest(string name, PostingKind postingKind, bool includeCategory, ReportInterval interval,
        bool comparePrevious, bool compareYear, bool showChart, bool expandable,
        IReadOnlyCollection<PostingKind>? postingKinds = null, ReportFavoriteFiltersDto? filters = null, bool useValutaDate = false)
        : this(name, postingKind, includeCategory, interval, 24, comparePrevious, compareYear, showChart, expandable, postingKinds, filters, useValutaDate) { }
}
