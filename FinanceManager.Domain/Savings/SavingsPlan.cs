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
    public string? ContractNumber { get; private set; }

    // Optional reference to a symbol attachment
    public Guid? SymbolAttachmentId { get; private set; }

    public SavingsPlan(Guid ownerUserId, string name, SavingsPlanType type, decimal? targetAmount, DateTime? targetDate, SavingsPlanInterval? interval, Guid? categoryId = null)
    {
        Id = Guid.NewGuid();
        OwnerUserId = ownerUserId;
        Name = name;
        Type = type;
        TargetAmount = targetAmount;
        TargetDate = targetDate;
        Interval = interval;
        CategoryId = categoryId;
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
    public void SetContractNumber(string? contractNumber) => ContractNumber = string.IsNullOrWhiteSpace(contractNumber) ? null : contractNumber.Trim();

    public void SetSymbolAttachment(Guid? attachmentId)
    {
        SymbolAttachmentId = attachmentId == Guid.Empty ? null : attachmentId;
    }

    /// <summary>
    /// Advances the TargetDate for recurring plans while the due date is reached or passed.
    /// Month-end rule:
    /// - If the original date was the last day of the month, the advanced date will also be set to the last day of the new month.
    /// - Otherwise, the day is capped to the last day of the new month (e.g. 31st -> 30th/28th/29th if needed).
    /// No business-day logic is applied.
    /// </summary>
    /// <param name="asOfUtc">Cutoff date (UTC) to compare against.</param>
    /// <returns>True if the target date was advanced at least once; otherwise false.</returns>
    public bool AdvanceTargetDateIfDue(DateTime asOfUtc)
    {
        if (Type != SavingsPlanType.Recurring)
        {
            return false;
        }
        if (!Interval.HasValue || !TargetDate.HasValue)
        {
            return false;
        }

        bool changed = false;
        while (TargetDate!.Value.Date <= asOfUtc.Date)
        {
            TargetDate = AddIntervalWithMonthEndRule(TargetDate.Value, Interval!.Value);
            changed = true;
        }
        return changed;
    }

    private static DateTime AddIntervalWithMonthEndRule(DateTime date, SavingsPlanInterval interval)
    {
        int monthsToAdd = interval switch
        {
            SavingsPlanInterval.Monthly => 1,
            SavingsPlanInterval.BiMonthly => 2,
            SavingsPlanInterval.Quarterly => 3,
            SavingsPlanInterval.SemiAnnually => 6,
            SavingsPlanInterval.Annually => 12,
            _ => throw new ArgumentOutOfRangeException(nameof(interval), interval, "Unsupported interval")
        };

        int originalDay = date.Day;
        bool wasMonthEnd = originalDay == DateTime.DaysInMonth(date.Year, date.Month);

        var added = date.AddMonths(monthsToAdd);
        int daysInNewMonth = DateTime.DaysInMonth(added.Year, added.Month);

        int newDay = wasMonthEnd
            ? daysInNewMonth
            : Math.Min(originalDay, daysInNewMonth);

        return new DateTime(added.Year, added.Month, newDay, date.Hour, date.Minute, date.Second, date.Millisecond, date.Kind);
    }
}