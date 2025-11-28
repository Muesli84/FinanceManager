namespace FinanceManager.Shared.Dtos.Reports;

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
