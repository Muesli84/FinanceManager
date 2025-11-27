namespace FinanceManager.Application.Reports;

public enum PostingExportFormat
{
    Csv,
    Xlsx
}

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

public interface IPostingExportService
{
    IAsyncEnumerable<PostingExportRow> QueryAsync(PostingExportQuery query, CancellationToken ct);
    Task<(string ContentType, string FileName, Stream Content)> GenerateAsync(PostingExportQuery query, CancellationToken ct);
    Task<int> CountAsync(PostingExportQuery query, CancellationToken ct);
    Task StreamCsvAsync(PostingExportQuery query, Stream output, CancellationToken ct);
}
