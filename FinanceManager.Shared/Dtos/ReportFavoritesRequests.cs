using System.ComponentModel.DataAnnotations;

namespace FinanceManager.Shared.Dtos;

/// <summary>
/// DTO describing filter sets used when creating or updating a report favorite.
/// </summary>
public sealed class ReportFavoriteFiltersApiDto
{
    /// <summary>List of account ids to include.</summary>
    public IReadOnlyCollection<Guid>? AccountIds { get; set; }
    /// <summary>List of contact ids to include.</summary>
    public IReadOnlyCollection<Guid>? ContactIds { get; set; }
    /// <summary>List of savings plan ids to include.</summary>
    public IReadOnlyCollection<Guid>? SavingsPlanIds { get; set; }
    /// <summary>List of security ids to include.</summary>
    public IReadOnlyCollection<Guid>? SecurityIds { get; set; }
    /// <summary>List of contact category ids to include.</summary>
    public IReadOnlyCollection<Guid>? ContactCategoryIds { get; set; }
    /// <summary>List of savings plan category ids to include.</summary>
    public IReadOnlyCollection<Guid>? SavingsPlanCategoryIds { get; set; }
    /// <summary>List of security category ids to include.</summary>
    public IReadOnlyCollection<Guid>? SecurityCategoryIds { get; set; }
    /// <summary>List of security sub types to include.</summary>
    public IReadOnlyCollection<int>? SecuritySubTypes { get; set; }
    /// <summary>Include dividend-related categories when true.</summary>
    public bool? IncludeDividendRelated { get; set; }
}

/// <summary>
/// Request payload to create a new report favorite configuration.
/// </summary>
public sealed class ReportFavoriteCreateApiRequest
{
    /// <summary>Display name of the favorite.</summary>
    [Required, MinLength(2), MaxLength(120)] public string Name { get; set; } = string.Empty;
    /// <summary>Primary posting kind for the report.</summary>
    [Range(0, 10)] public PostingKind PostingKind { get; set; }
    /// <summary>Include category breakdown when true.</summary>
    public bool IncludeCategory { get; set; }
    /// <summary>Aggregation interval value.</summary>
    [Range(0, 10)] public int Interval { get; set; }
    /// <summary>Number of periods to take (default 24).</summary>
    [Range(1,120)] public int Take { get; set; } = 24;
    /// <summary>Compare to previous period when true.</summary>
    public bool ComparePrevious { get; set; }
    /// <summary>Compare to previous year when true.</summary>
    public bool CompareYear { get; set; }
    /// <summary>Show chart visualization when true.</summary>
    public bool ShowChart { get; set; }
    /// <summary>Allow expanding to show detailed view.</summary>
    public bool Expandable { get; set; } = true;
    /// <summary>Optional multi-kind selection for the report.</summary>
    public IReadOnlyCollection<PostingKind>? PostingKinds { get; set; }
    /// <summary>Optional filter sets.</summary>
    public ReportFavoriteFiltersApiDto? Filters { get; set; }
    /// <summary>Aggregate by ValutaDate when true.</summary>
    public bool UseValutaDate { get; set; }
}

/// <summary>
/// Request payload to update an existing report favorite configuration.
/// </summary>
public sealed class ReportFavoriteUpdateApiRequest
{
    /// <summary>Display name of the favorite.</summary>
    [Required, MinLength(2), MaxLength(120)] public string Name { get; set; } = string.Empty;
    /// <summary>Primary posting kind for the report.</summary>
    [Range(0, 10)] public PostingKind PostingKind { get; set; }
    /// <summary>Include category breakdown when true.</summary>
    public bool IncludeCategory { get; set; }
    /// <summary>Aggregation interval value.</summary>
    [Range(0, 10)] public int Interval { get; set; }
    /// <summary>Number of periods to take (default 24).</summary>
    [Range(1,120)] public int Take { get; set; } = 24;
    /// <summary>Compare to previous period when true.</summary>
    public bool ComparePrevious { get; set; }
    /// <summary>Compare to previous year when true.</summary>
    public bool CompareYear { get; set; }
    /// <summary>Show chart visualization when true.</summary>
    public bool ShowChart { get; set; }
    /// <summary>Allow expanding to show detailed view.</summary>
    public bool Expandable { get; set; } = true;
    /// <summary>Optional multi-kind selection for the report.</summary>
    public IReadOnlyCollection<PostingKind>? PostingKinds { get; set; }
    /// <summary>Optional filter sets.</summary>
    public ReportFavoriteFiltersApiDto? Filters { get; set; }
    /// <summary>Aggregate by ValutaDate when true.</summary>
    public bool UseValutaDate { get; set; }
}
