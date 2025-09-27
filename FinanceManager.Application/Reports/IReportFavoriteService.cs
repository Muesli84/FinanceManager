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
    IReadOnlyCollection<Guid>? SecurityCategoryIds
);

public sealed record ReportFavoriteDto(
    Guid Id,
    string Name,
    int PostingKind,
    bool IncludeCategory,
    ReportInterval Interval,
    bool ComparePrevious,
    bool CompareYear,
    bool ShowChart,
    bool Expandable,
    DateTime CreatedUtc,
    DateTime? ModifiedUtc,
    IReadOnlyCollection<int> PostingKinds, // multi support (at least one, falls back to single PostingKind if none stored)
    ReportFavoriteFiltersDto? Filters
);

public sealed record ReportFavoriteCreateRequest(
    string Name,
    int PostingKind,
    bool IncludeCategory,
    ReportInterval Interval,
    bool ComparePrevious,
    bool CompareYear,
    bool ShowChart,
    bool Expandable,
    IReadOnlyCollection<int>? PostingKinds = null, // optional multi list
    ReportFavoriteFiltersDto? Filters = null
);

public sealed record ReportFavoriteUpdateRequest(
    string Name,
    int PostingKind,
    bool IncludeCategory,
    ReportInterval Interval,
    bool ComparePrevious,
    bool CompareYear,
    bool ShowChart,
    bool Expandable,
    IReadOnlyCollection<int>? PostingKinds = null, // optional multi list
    ReportFavoriteFiltersDto? Filters = null
);
