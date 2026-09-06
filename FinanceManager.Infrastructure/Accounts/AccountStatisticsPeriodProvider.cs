using FinanceManager.Application.Accounts;
using Microsoft.EntityFrameworkCore;

namespace FinanceManager.Infrastructure.Accounts;

/// <summary>
/// Computes account statistics period boundaries from the fixed clock and the user's configured time zone.
/// </summary>
public sealed class AccountStatisticsPeriodProvider : IAccountStatisticsPeriodProvider
{
    private readonly AppDbContext _db;
    private readonly TimeProvider _timeProvider;
    private readonly ITimeZoneResolver _timeZoneResolver;

    /// <summary>
    /// Initializes a new instance of the <see cref="AccountStatisticsPeriodProvider"/> class.
    /// </summary>
    public AccountStatisticsPeriodProvider(AppDbContext db, TimeProvider timeProvider, ITimeZoneResolver timeZoneResolver)
    {
        _db = db;
        _timeProvider = timeProvider;
        _timeZoneResolver = timeZoneResolver;
    }

    /// <inheritdoc />
    public async Task<AccountStatisticsPeriod> GetPeriodAsync(Guid ownerUserId, CancellationToken ct)
    {
        var timeZoneId = await _db.Users
            .AsNoTracking()
            .Where(u => u.Id == ownerUserId)
            .Select(u => u.TimeZoneId)
            .FirstOrDefaultAsync(ct);

        var timeZone = _timeZoneResolver.ResolveOrUtc(timeZoneId);
        var localNow = TimeZoneInfo.ConvertTime(_timeProvider.GetUtcNow(), timeZone);
        var today = DateTime.SpecifyKind(localNow.Date, DateTimeKind.Unspecified);
        return new AccountStatisticsPeriod(
            today,
            new DateTime(today.Year, 1, 1),
            new DateTime(today.Year, today.Month, 1),
            today.AddDays(1));
    }
}
