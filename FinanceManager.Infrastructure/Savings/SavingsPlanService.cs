using FinanceManager.Application.Savings;
using FinanceManager.Domain; // added for PostingKind
using FinanceManager.Domain.Savings;
using FinanceManager.Shared.Dtos;
using Microsoft.EntityFrameworkCore;

namespace FinanceManager.Infrastructure.Savings;

public sealed class SavingsPlanService : ISavingsPlanService
{
    private readonly AppDbContext _db;
    public SavingsPlanService(AppDbContext db) { _db = db; }

    public async Task<IReadOnlyList<SavingsPlanDto>> ListAsync(Guid ownerUserId, bool onlyActive, CancellationToken ct)
    {
        var query = _db.SavingsPlans.AsNoTracking().Where(p => p.OwnerUserId == ownerUserId);
        if (onlyActive) { query = query.Where(p => p.IsActive); }
        return await query
            .OrderBy(p => p.Name)
            .Select(p => new SavingsPlanDto(p.Id, p.Name, p.Type, p.TargetAmount, p.TargetDate, p.Interval, p.IsActive, p.CreatedUtc, p.ArchivedUtc, p.CategoryId, p.ContractNumber))
            .ToListAsync(ct);
    }

    public async Task<SavingsPlanDto?> GetAsync(Guid id, Guid ownerUserId, CancellationToken ct)
    {
        var plan = await _db.SavingsPlans.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id && p.OwnerUserId == ownerUserId, ct);
        return plan == null ? null : new SavingsPlanDto(plan.Id, plan.Name, plan.Type, plan.TargetAmount, plan.TargetDate, plan.Interval, plan.IsActive, plan.CreatedUtc, plan.ArchivedUtc, plan.CategoryId, plan.ContractNumber);
    }

    public async Task<SavingsPlanDto> CreateAsync(Guid ownerUserId, string name, SavingsPlanType type, decimal? targetAmount, DateTime? targetDate, SavingsPlanInterval? interval, Guid? categoryId, string? contractNumber, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            var exists = await _db.SavingsPlans.AnyAsync(p => p.OwnerUserId == ownerUserId && p.Name == name, ct);
            if (exists) { throw new ArgumentException("Savings plan name must be unique per user", nameof(name)); }
        }
        var plan = new SavingsPlan(ownerUserId, name, type, targetAmount, targetDate, interval, categoryId);
        plan.SetContractNumber(contractNumber);
        _db.SavingsPlans.Add(plan);
        await _db.SaveChangesAsync(ct);
        return new SavingsPlanDto(plan.Id, plan.Name, plan.Type, plan.TargetAmount, plan.TargetDate, plan.Interval, plan.IsActive, plan.CreatedUtc, plan.ArchivedUtc, plan.CategoryId, plan.ContractNumber);
    }

    public async Task<SavingsPlanDto?> UpdateAsync(Guid id, Guid ownerUserId, string name, SavingsPlanType type, decimal? targetAmount, DateTime? targetDate, SavingsPlanInterval? interval, Guid? categoryId, string? contractNumber, CancellationToken ct)
    {
        var plan = await _db.SavingsPlans.FirstOrDefaultAsync(p => p.Id == id && p.OwnerUserId == ownerUserId, ct);
        if (plan == null) { return null; }
        if (!string.Equals(plan.Name, name, StringComparison.OrdinalIgnoreCase))
        {
            var exists = await _db.SavingsPlans.AnyAsync(p => p.OwnerUserId == ownerUserId && p.Name == name && p.Id != id, ct);
            if (exists) { throw new ArgumentException("Savings plan name must be unique per user", nameof(name)); }
        }
        plan.Rename(name);
        plan.ChangeType(type);
        plan.SetTarget(targetAmount, targetDate);
        plan.SetInterval(interval);
        plan.SetCategory(categoryId);
        plan.SetContractNumber(contractNumber);
        await _db.SaveChangesAsync(ct);
        return new SavingsPlanDto(plan.Id, plan.Name, plan.Type, plan.TargetAmount, plan.TargetDate, plan.Interval, plan.IsActive, plan.CreatedUtc, plan.ArchivedUtc, plan.CategoryId, plan.ContractNumber);
    }

    public async Task<bool> ArchiveAsync(Guid id, Guid ownerUserId, CancellationToken ct)
    {
        var plan = await _db.SavingsPlans.FirstOrDefaultAsync(p => p.Id == id && p.OwnerUserId == ownerUserId, ct);
        if (plan == null || !plan.IsActive) { return false; }
        plan.Archive();
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DeleteAsync(Guid id, Guid ownerUserId, CancellationToken ct)
    {
        var plan = await _db.SavingsPlans.FirstOrDefaultAsync(p => p.Id == id && p.OwnerUserId == ownerUserId, ct);
        if (plan == null) { return false; }

        // Business rules:
        // 1. Only archived plans can be deleted (avoid accidental data loss of active plans).
        if (plan.IsActive) { return false; }

        // 2. Prevent deletion if referenced by committed statement entries.
        bool hasStatementEntries = await _db.StatementEntries.AsNoTracking().AnyAsync(e => e.SavingsPlanId == id, ct);
        if (hasStatementEntries) { return false; }

        // 3. Prevent deletion if referenced by draft entries (user must remove assignments first).
        bool hasDraftEntries = await _db.StatementDraftEntries.AsNoTracking().AnyAsync(e => e.SavingsPlanId == id, ct);
        if (hasDraftEntries) { return false; }

        // 4. Prevent deletion if postings reference the plan (future feature; safety for already created postings).
        bool hasPostings = await _db.Postings.AsNoTracking().AnyAsync(p => p.SavingsPlanId == id, ct);
        if (hasPostings) { return false; }

        _db.SavingsPlans.Remove(plan);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<SavingsPlanAnalysisDto> AnalyzeAsync(Guid id, Guid ownerUserId, CancellationToken ct)
    {
        var plan = await _db.SavingsPlans.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id && p.OwnerUserId == ownerUserId, ct);
        if (plan == null)
        {
            return new SavingsPlanAnalysisDto(id, false, null, null, 0m, 0m, 0);
        }

        // If no target defined, cannot analyze meaningful reachability
        if (plan.TargetAmount is null || plan.TargetDate is null)
        {
            return new SavingsPlanAnalysisDto(id, true, plan.TargetAmount, plan.TargetDate, 0m, 0m, 0);
        }

        var today = DateTime.Today;
        var endDate = plan.TargetDate.Value.Date;
        var monthsRemaining = Math.Max(0, ((endDate.Year - today.Year) * 12 + endDate.Month - today.Month));

        // Past contributions up to today for this plan
        var history = await _db.Postings.AsNoTracking()
            .Where(p => p.SavingsPlanId == id && p.Kind == PostingKind.SavingsPlan && p.BookingDate <= today)
            .Select(p => new { p.BookingDate, p.Amount })
            .ToListAsync(ct);

        var accumulated = history.Sum(x => x.Amount);

        var target = plan.TargetAmount.Value;

        if (monthsRemaining <= 0)
        {
            var reached = accumulated >= target;
            return new SavingsPlanAnalysisDto(id, reached, target, endDate, accumulated, 0m, 0);
        }

        // Average monthly saving based on history grouped by month
        var monthlyTotals = history
            .GroupBy(x => new { x.BookingDate.Year, x.BookingDate.Month })
            .Select(g => g.Sum(x => x.Amount))
            .ToList();
        var averagePerMonth = monthlyTotals.Count > 0 ? monthlyTotals.Average() : 0m;

        var forecast = averagePerMonth * monthsRemaining;
        var totalExpected = accumulated + forecast;
        var reachable = totalExpected >= target;

        var remaining = Math.Max(0m, target - accumulated);
        var requiredMonthly = monthsRemaining > 0 ? remaining / monthsRemaining : 0m;

        return new SavingsPlanAnalysisDto(id, reachable, target, endDate, accumulated, requiredMonthly, monthsRemaining);
    }
}