namespace FinanceManager.Shared.Dtos.Statements;

/// <summary>
/// Defines how imported statement entries are split into drafts.
/// </summary>
public enum ImportSplitMode : short
{
    /// <summary>Always use a fixed number of entries per draft.</summary>
    FixedSize = 0,
    /// <summary>Split drafts by calendar month.</summary>
    Monthly = 1,
    /// <summary>Use monthly splits unless a fixed size threshold is reached.</summary>
    MonthlyOrFixed = 2
}
