namespace FinanceManager.Shared.Dtos.SavingsPlans;

/// <summary>
/// Recurrence intervals for savings plans.
/// </summary>
public enum SavingsPlanInterval
{
    /// <summary>Monthly.</summary>
    Monthly,
    /// <summary>Every two months.</summary>
    BiMonthly,
    /// <summary>Quarterly.</summary>
    Quarterly,
    /// <summary>Semi-annually.</summary>
    SemiAnnually,
    /// <summary>Annually.</summary>
    Annually
}