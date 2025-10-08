namespace FinanceManager.Domain.Reports;

/// <summary>
/// Known predefined KPIs that can be placed on the home dashboard.
/// </summary>
public enum HomeKpiPredefined
{
    AccountsAggregates = 0,
    SavingsPlanAggregates = 1,
    SecuritiesDividends = 2,
    // Count KPIs
    ActiveSavingsPlansCount = 10,
    ContactsCount = 11,
    SecuritiesCount = 12,
    OpenStatementDraftsCount = 13
}
