using FinanceManager.Application.Accounts;
using FinanceManager.Domain.Users;
using FinanceManager.Infrastructure;
using FinanceManager.Infrastructure.Accounts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinanceManager.Tests.Accounts;

/// <summary>
/// Tests local account statistics period boundary calculation.
/// </summary>
public sealed class AccountStatisticsPeriodProviderTests
{
    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        public FixedTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;
    }

    private sealed class FixedZoneResolver : ITimeZoneResolver
    {
        public TimeZoneInfo ResolveOrUtc(string? timeZoneId)
            => TimeZoneInfo.CreateCustomTimeZone("Europe/Berlin", TimeSpan.FromHours(2), "Europe/Berlin", "Europe/Berlin");
    }

    /// <summary>
    /// Ensures a fixed UTC instant is converted to the user's local date before period starts are produced.
    /// </summary>
    [Fact]
    public async Task AccountStatisticsPeriodProvider_UsesFixedClockAndEuropeBerlinTimeZone()
    {
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        var sp = services.BuildServiceProvider();
        var db = sp.GetRequiredService<AppDbContext>();
        var user = new User("period-user", "hash");
        user.SetTimeZoneId("Europe/Berlin");
        db.Users.Add(user);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var sut = new AccountStatisticsPeriodProvider(
            db,
            new FixedTimeProvider(new DateTimeOffset(2026, 3, 31, 22, 30, 0, TimeSpan.Zero)),
            new FixedZoneResolver());

        var period = await sut.GetPeriodAsync(user.Id, TestContext.Current.CancellationToken);

        Assert.Equal(new DateTime(2026, 4, 1), period.Today);
        Assert.Equal(new DateTime(2026, 1, 1), period.YearStart);
        Assert.Equal(new DateTime(2026, 4, 1), period.MonthStart);
        Assert.Equal(new DateTime(2026, 4, 2), period.TomorrowExclusive);
        Assert.Equal(DateTimeKind.Unspecified, period.Today.Kind);
    }

    /// <summary>
    /// Verifies that the default resolver falls back deterministically to UTC for invalid identifiers.
    /// </summary>
    [Fact]
    public void TimeZoneResolver_InvalidTimeZone_FallsBackToUtc()
    {
        var sut = new TimeZoneResolver(NullLogger<TimeZoneResolver>.Instance);

        var zone = sut.ResolveOrUtc("Missing/Zone");

        Assert.Equal(TimeZoneInfo.Utc.Id, zone.Id);
    }

    /// <summary>
    /// Verifies that missing or blank identifiers fall back deterministically to UTC.
    /// </summary>
    /// <param name="timeZoneId">The time zone id.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TimeZoneResolver_MissingTimeZone_FallsBackToUtc(string? timeZoneId)
    {
        var sut = new TimeZoneResolver(NullLogger<TimeZoneResolver>.Instance);

        var zone = sut.ResolveOrUtc(timeZoneId);

        Assert.Equal(TimeZoneInfo.Utc.Id, zone.Id);
    }
}
