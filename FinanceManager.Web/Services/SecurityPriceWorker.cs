using FinanceManager.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace FinanceManager.Web.Services;

public sealed class SecurityPriceWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SecurityPriceWorker> _logger;
    private readonly IPriceProvider _prices;

    public SecurityPriceWorker(IServiceScopeFactory scopeFactory, ILogger<SecurityPriceWorker> logger, IPriceProvider prices)
    {
        _scopeFactory = scopeFactory; _logger = logger; _prices = prices;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // run periodically (every hour)
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (RequestLimitExceededException ex)
            {
                _logger.LogWarning(ex, "AlphaVantage limit exceeded. Pausing worker run.");                
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SecurityPriceWorker run failed");
            }
            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var today = DateTime.UtcNow.Date;
        var endInclusive = today.AddDays(-1);
        while (endInclusive.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            endInclusive = endInclusive.AddDays(-1);
        }

        var securities = await db.Securities.AsNoTracking()
            .Where(s => s.IsActive && s.AlphaVantageCode != null)
            .ToListAsync(ct);

        foreach (var sec in securities)
        {
            var last = await db.SecurityPrices.AsNoTracking()
                .Where(p => p.SecurityId == sec.Id)
                .OrderByDescending(p => p.Date)
                .Select(p => p.Date)
                .FirstOrDefaultAsync(ct);

            var startExclusive = last == default ? endInclusive.AddYears(-2) : last; 
            if (endInclusive <= startExclusive) { continue; }

            var prices = await _prices.GetDailyPricesAsync(sec.AlphaVantageCode!, startExclusive, endInclusive, ct);
            if (prices.Count == 0) { continue; }
            foreach (var (date, close) in prices)
            {
                if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) continue;
                db.SecurityPrices.Add(new FinanceManager.Domain.Securities.SecurityPrice(sec.Id, date, close));
            }
            await db.SaveChangesAsync(ct);
        }
    }
}