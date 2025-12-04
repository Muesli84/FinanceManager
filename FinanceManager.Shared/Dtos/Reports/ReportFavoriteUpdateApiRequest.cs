using System.ComponentModel.DataAnnotations;

namespace FinanceManager.Shared.Dtos.Reports;

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
    [Range(1, 120)] public int Take { get; set; } = 24;
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
    public ReportFavoriteFiltersDto? Filters { get; set; }
    /// <summary>Aggregate by ValutaDate when true.</summary>
    public bool UseValutaDate { get; set; }
}
