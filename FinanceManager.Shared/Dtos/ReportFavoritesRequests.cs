using System.ComponentModel.DataAnnotations;

namespace FinanceManager.Shared.Dtos;

public sealed class ReportFavoriteFiltersApiDto
{
    public IReadOnlyCollection<Guid>? AccountIds { get; set; }
    public IReadOnlyCollection<Guid>? ContactIds { get; set; }
    public IReadOnlyCollection<Guid>? SavingsPlanIds { get; set; }
    public IReadOnlyCollection<Guid>? SecurityIds { get; set; }
    public IReadOnlyCollection<Guid>? ContactCategoryIds { get; set; }
    public IReadOnlyCollection<Guid>? SavingsPlanCategoryIds { get; set; }
    public IReadOnlyCollection<Guid>? SecurityCategoryIds { get; set; }
    public IReadOnlyCollection<int>? SecuritySubTypes { get; set; }
    public bool? IncludeDividendRelated { get; set; }
}

public sealed class ReportFavoriteCreateApiRequest
{
    [Required, MinLength(2), MaxLength(120)] public string Name { get; set; } = string.Empty;
    [Range(0, 10)] public PostingKind PostingKind { get; set; }
    public bool IncludeCategory { get; set; }
    [Range(0, 10)] public int Interval { get; set; }
    [Range(1,120)] public int Take { get; set; } = 24;
    public bool ComparePrevious { get; set; }
    public bool CompareYear { get; set; }
    public bool ShowChart { get; set; }
    public bool Expandable { get; set; } = true;
    public IReadOnlyCollection<PostingKind>? PostingKinds { get; set; }
    public ReportFavoriteFiltersApiDto? Filters { get; set; }
    public bool UseValutaDate { get; set; }
}

public sealed class ReportFavoriteUpdateApiRequest
{
    [Required, MinLength(2), MaxLength(120)] public string Name { get; set; } = string.Empty;
    [Range(0, 10)] public PostingKind PostingKind { get; set; }
    public bool IncludeCategory { get; set; }
    [Range(0, 10)] public int Interval { get; set; }
    [Range(1,120)] public int Take { get; set; } = 24;
    public bool ComparePrevious { get; set; }
    public bool CompareYear { get; set; }
    public bool ShowChart { get; set; }
    public bool Expandable { get; set; } = true;
    public IReadOnlyCollection<PostingKind>? PostingKinds { get; set; }
    public ReportFavoriteFiltersApiDto? Filters { get; set; }
    public bool UseValutaDate { get; set; }
}
