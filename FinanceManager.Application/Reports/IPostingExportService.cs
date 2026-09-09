namespace FinanceManager.Application.Reports;

/// <summary>
/// Supported file formats for posting exports.
/// </summary>
public enum PostingExportFormat
{
    /// <summary>
    /// Comma-separated values.
    /// </summary>
    Csv,

    /// <summary>
    /// Excel .xlsx format.
    /// </summary>
    Xlsx
}

/// <summary>
/// Query object used to request an export of postings.
/// </summary>
/// <param name="OwnerUserId">Owner user identifier performing the export.</param>
/// <param name="ContextKind">Posting kind that defines the context (Account/Contact/SavingsPlan/Security).</param>
/// <param name="ContextId">Context entity id.</param>
/// <param name="Format">Export format.</param>
/// <param name="MaxRows">Maximum number of rows to include.</param>
/// <param name="From">Optional from date filter.</param>
/// <param name="To">Optional to date filter.</param>
/// <param name="Q">Optional search query.</param>
/// <returns>The result.</returns>
public sealed record PostingExportQuery(
    Guid OwnerUserId,
    PostingKind ContextKind,
    Guid ContextId,
    PostingExportFormat Format,
    int MaxRows,
    DateTime? From = null,
    DateTime? To = null,
    string? Q = null
);

/// <summary>
/// Single row representation used when streaming or generating posting export content.
/// </summary>
/// <param name="BookingDate">Booking date of the posting.</param>
/// <param name="ValutaDate">Valuta/value date of the posting.</param>
/// <param name="Amount">Monetary amount.</param>
/// <param name="Kind">Posting kind.</param>
/// <param name="Subject">Optional subject/short description.</param>
/// <param name="RecipientName">Optional recipient name.</param>
/// <param name="Description">Optional description text.</param>
/// <param name="AccountId">Optional account id referenced.</param>
/// <param name="ContactId">Optional contact id referenced.</param>
/// <param name="SavingsPlanId">Optional savings plan id referenced.</param>
/// <param name="SecurityId">Optional security id referenced.</param>
/// <param name="SecuritySubType">Optional security posting subtype.</param>
/// <param name="Quantity">Optional quantity for security postings.</param>
/// <returns>The result.</returns>
public sealed record PostingExportRow(
    DateTime BookingDate,
    DateTime ValutaDate,
    decimal Amount,
    PostingKind Kind,
    string? Subject,
    string? RecipientName,
    string? Description,
    Guid? AccountId,
    Guid? ContactId,
    Guid? SavingsPlanId,
    Guid? SecurityId,
    SecurityPostingSubType? SecuritySubType,
    decimal? Quantity
);

/// <summary>
/// Service supporting querying and generation of posting exports in various formats.
/// </summary>
public interface IPostingExportService
{
    /// <summary>
    /// Streams posting export rows matching the given query.
    /// </summary>
    /// <param name="query">Query describing the export criteria and limits.</param>
    /// <param name="ct">Cancellation token to cancel the streaming operation.</param>
    /// <returns>An async sequence of <see cref="PostingExportRow"/> instances matching the query.</returns>
    IAsyncEnumerable<PostingExportRow> QueryAsync(PostingExportQuery query, CancellationToken ct);

    /// <summary>
    /// Generates a file for the given query and returns content type, file name and a readable stream with the generated content.
    /// Caller is responsible for disposing the returned <see cref="Stream"/> when finished.
    /// </summary>
    /// <param name="query">Query describing the export to generate.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A tuple containing the content type (e.g. "text/csv"), the suggested file name and a stream with file content.
    /// The returned stream is readable from the beginning.
    /// </returns>
    Task<(string ContentType, string FileName, Stream Content)> GenerateAsync(PostingExportQuery query, CancellationToken ct);

    /// <summary>
    /// Counts the total number of rows that would be produced for the given export query.
    /// </summary>
    /// <param name="query">Export query to evaluate.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Total number of matching rows (non-negative integer).</returns>
    Task<int> CountAsync(PostingExportQuery query, CancellationToken ct);

    /// <summary>
    /// Streams CSV content for the given query directly to the provided output stream.
    /// This method writes CSV text into the supplied <paramref name="output"/> stream and does not return a separate buffer.
    /// </summary>
    /// <param name="query">Export query describing which rows to include.</param>
    /// <param name="output">Destination stream to which CSV content will be written. The caller remains owner of the stream.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes when streaming has finished or an exception is thrown.</returns>
    Task StreamCsvAsync(PostingExportQuery query, Stream output, CancellationToken ct);
}
