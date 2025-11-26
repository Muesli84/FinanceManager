namespace FinanceManager.Shared.Dtos;

/// <summary>
/// Identifies the type/source of a KPI tile on the home dashboard.
/// </summary>
public enum HomeKpiKind
{
    Predefined = 0,
    ReportFavorite = 1
}

/// <summary>
/// Defines how a KPI tile should visualize data.
/// </summary>
public enum HomeKpiDisplayMode
{
    TotalOnly = 0,
    TotalWithComparisons = 1,
    ReportGraph = 2
}

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
