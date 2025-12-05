using FinanceManager.Domain.Postings;

namespace FinanceManager.Application.Aggregates;

public interface IPostingAggregateService
{
    Task UpsertForPostingAsync(Posting posting, CancellationToken ct);
    Task RebuildForUserAsync(Guid userId, Action<int, int> progressCallback, CancellationToken ct);
}
