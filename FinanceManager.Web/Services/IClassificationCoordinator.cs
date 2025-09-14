using FinanceManager.Application.Statements;

namespace FinanceManager.Web.Services;

public interface IClassificationCoordinator
{
    Task<ClassificationStatus> ProcessAsync(Guid userId, TimeSpan maxDuration, CancellationToken ct);
    ClassificationStatus? GetStatus(Guid userId);
}

public sealed record ClassificationStatus(bool Running, int Processed, int Total, string? Message);
