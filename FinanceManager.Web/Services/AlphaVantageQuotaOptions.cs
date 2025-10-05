namespace FinanceManager.Web.Services;

public sealed class AlphaVantageQuotaOptions
{
    // Max. Anzahl Symbole pro Worker-Lauf
    public int MaxSymbolsPerRun { get; set; } = 10;

    // Requests pro Minute (Pacing zwischen Symbolen). 0/negativ => kein Pacing.
    public int RequestsPerMinute { get; set; } = 4;
}