namespace FinanceManager.Shared.Dtos.HomeKpi;

public enum HomeKpiKind
{
    Predefined = 0,
    ReportFavorite = 1
}

public enum HomeKpiDisplayMode
{
    TotalOnly = 0,
    TotalWithComparisons = 1,
    ReportGraph = 2
}

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
