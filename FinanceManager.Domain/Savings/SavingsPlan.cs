using FinanceManager.Shared.Dtos;
using System;

namespace FinanceManager.Domain.Savings;

public sealed class SavingsPlan
{
    public Guid Id { get; private set; }
    public Guid OwnerUserId { get; private set; }
    public string Name { get; private set; }
    public SavingsPlanType Type { get; private set; }
    public decimal? TargetAmount { get; private set; }
    public DateTime? TargetDate { get; private set; }
    public SavingsPlanInterval? Interval { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime? ArchivedUtc { get; private set; }
    public Guid? CategoryId { get; private set; }

    public SavingsPlan(Guid ownerUserId, string name, SavingsPlanType type, decimal? targetAmount, DateTime? targetDate, SavingsPlanInterval? interval)
    {
        Id = Guid.NewGuid();
        OwnerUserId = ownerUserId;
        Name = name;
        Type = type;
        TargetAmount = targetAmount;
        TargetDate = targetDate;
        Interval = interval;
        IsActive = true;
        CreatedUtc = DateTime.UtcNow;
    }

    public void Archive()
    {
        IsActive = false;
        ArchivedUtc = DateTime.UtcNow;
    }

    public void Rename(string name) => Name = name;
    public void ChangeType(SavingsPlanType type) => Type = type;
    public void SetTarget(decimal? amount, DateTime? date) { TargetAmount = amount; TargetDate = date; }
    public void SetInterval(SavingsPlanInterval? interval) => Interval = interval;
    public void SetCategory(Guid? categoryId) => CategoryId = categoryId;
}