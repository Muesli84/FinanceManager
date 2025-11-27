namespace FinanceManager.Infrastructure.Statements;

public sealed partial class StatementDraftService
{
    // Delegate aggregates to shared service injected in the main partial file
    private async Task UpsertAggregatesAsync(Domain.Postings.Posting posting, CancellationToken ct)
    {
        await _aggregateService.UpsertForPostingAsync(posting, ct);
    }
}
