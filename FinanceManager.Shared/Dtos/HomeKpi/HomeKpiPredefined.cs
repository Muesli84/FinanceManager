namespace FinanceManager.Shared.Dtos.HomeKpi;

/// <summary>
/// Known predefined KPIs that can be placed on the home dashboard.
/// </summary>
public enum HomeKpiPredefined
{
    /// <summary>Aggregated balances/amounts for accounts.</summary>
    AccountsAggregates = 0,
    /// <summary>Aggregated amounts for savings plans.</summary>
    SavingsPlanAggregates = 1,
    /// <summary>Dividend totals for securities.</summary>
    SecuritiesDividends = 2,
    // Count KPIs
    /// <summary>Number of active savings plans.</summary>
    ActiveSavingsPlansCount = 10,
    /// <summary>Total number of contacts.</summary>
    ContactsCount = 11,
    /// <summary>Total number of securities.</summary>
    SecuritiesCount = 12,
    /// <summary>Number of open statement drafts.</summary>
    OpenStatementDraftsCount = 13
}
