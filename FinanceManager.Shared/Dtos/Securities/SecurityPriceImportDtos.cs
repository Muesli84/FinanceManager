namespace FinanceManager.Shared.Dtos.Securities;

/// <summary>
/// Context used to select a provider-specific import service.
/// </summary>
/// <param name="Provider">Optional provider hint (for example "ing").</param>
/// <param name="FileName">Uploaded file name.</param>
/// <param name="ContentType">Optional content type of the uploaded file.</param>
/// <returns>The result.</returns>
public sealed record SecurityPriceImportContext(string? Provider, string FileName, string? ContentType);

/// <summary>
/// Metadata extracted by a provider-specific content inspection.
/// </summary>
/// <param name="Provider">Resolved provider key (for example "ing").</param>
/// <param name="ServiceKey">Service key used for execution routing.</param>
/// <param name="ServiceDisplayName">Human-readable service name.</param>
/// <param name="DetectedSecurityName">Security name extracted from the file header when available.</param>
/// <returns>The result.</returns>
public sealed record SecurityPriceImportInspectionResult(
    string Provider,
    string ServiceKey,
    string ServiceDisplayName,
    string? DetectedSecurityName);

/// <summary>
/// Represents a single importable price row normalized to a trading day.
/// </summary>
/// <param name="Date">Trading day (date component only).</param>
/// <param name="Close">Close price for the trading day.</param>
/// <param name="SourceLine">Original CSV line number.</param>
/// <returns>The result.</returns>
public sealed record SecurityPriceImportItem(DateTime Date, decimal Close, int SourceLine);

/// <summary>
/// Represents one validation or parsing error from an import file.
/// </summary>
/// <param name="LineNumber">CSV line number where the error occurred.</param>
/// <param name="Message">Human-readable error message.</param>
/// <returns>The result.</returns>
public sealed record SecurityPriceImportErrorDto(int LineNumber, string Message);

/// <summary>
/// Result of a security price import.
/// </summary>
/// <param name="Inserted">Number of newly created daily prices.</param>
/// <param name="Updated">Number of existing daily prices updated with a new close value.</param>
/// <param name="Unchanged">Number of existing daily prices that already had the same close value.</param>
/// <param name="Skipped">Number of skipped input lines (for example empty or invalid lines).</param>
/// <param name="Errors">Collection of row-level errors encountered while parsing.</param>
/// <returns>The result.</returns>
public sealed record SecurityPriceImportResultDto(
    int Inserted,
    int Updated,
    int Unchanged,
    int Skipped,
    IReadOnlyList<SecurityPriceImportErrorDto> Errors);
