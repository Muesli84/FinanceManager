namespace FinanceManager.Web.Services;

public interface IBookingCoordinator
{
    Task<BookingStatus> ProcessAsync(Guid userId, bool ignoreWarnings, bool abortOnFirstIssue, TimeSpan maxDuration, CancellationToken ct);
    BookingStatus? GetStatus(Guid userId);
    void Cancel(Guid userId);
}

public sealed record BookingIssue(Guid DraftId, Guid? EntryId, string Code, string Message);

public sealed record BookingStatus(bool Running, int Processed, int Failed, int Total, string? Message, int Warnings, int Errors, IReadOnlyList<BookingIssue> ErrorDetails);