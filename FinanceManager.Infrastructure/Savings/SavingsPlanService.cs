using FinanceManager.Application.Savings;
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
            .Select(p => new SavingsPlanDto(p.Id, p.Name, p.Type, p.TargetAmount, p.TargetDate, p.Interval, p.IsActive, p.CreatedUtc, p.ArchivedUtc, p.CategoryId))
            .ToListAsync(ct);
    }

    public async Task<SavingsPlanDto?> GetAsync(Guid id, Guid ownerUserId, CancellationToken ct)
    {
        var plan = await _db.SavingsPlans.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id && p.OwnerUserId == ownerUserId, ct);
        return plan == null ? null : new SavingsPlanDto(plan.Id, plan.Name, plan.Type, plan.TargetAmount, plan.TargetDate, plan.Interval, plan.IsActive, plan.CreatedUtc, plan.ArchivedUtc, plan.CategoryId);
    }

    public async Task<SavingsPlanDto> CreateAsync(Guid ownerUserId, string name, SavingsPlanType type, decimal? targetAmount, DateTime? targetDate, SavingsPlanInterval? interval, Guid? categoryId, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            var exists = await _db.SavingsPlans.AnyAsync(p => p.OwnerUserId == ownerUserId && p.Name == name, ct);
            if (exists) { throw new ArgumentException("Savings plan name must be unique per user", nameof(name)); }
        }
        var plan = new SavingsPlan(ownerUserId, name, type, targetAmount, targetDate, interval, categoryId);
        _db.SavingsPlans.Add(plan);
        await _db.SaveChangesAsync(ct);
        return new SavingsPlanDto(plan.Id, plan.Name, plan.Type, plan.TargetAmount, plan.TargetDate, plan.Interval, plan.IsActive, plan.CreatedUtc, plan.ArchivedUtc, plan.CategoryId);
    }

    public async Task<SavingsPlanDto?> UpdateAsync(Guid id, Guid ownerUserId, string name, SavingsPlanType type, decimal? targetAmount, DateTime? targetDate, SavingsPlanInterval? interval, Guid? categoryId, CancellationToken ct)
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
        await _db.SaveChangesAsync(ct);
        return new SavingsPlanDto(plan.Id, plan.Name, plan.Type, plan.TargetAmount, plan.TargetDate, plan.Interval, plan.IsActive, plan.CreatedUtc, plan.ArchivedUtc, plan.CategoryId);
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
}