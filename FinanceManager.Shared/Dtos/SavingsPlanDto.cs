using System;

namespace FinanceManager.Shared.Dtos;

public sealed class SavingsPlanDto
{
    public SavingsPlanDto()
    {

    }
    public SavingsPlanDto(Guid id, string name, SavingsPlanType type, decimal? targetAmount, DateTime? targetDate, SavingsPlanInterval? interval, bool isActive, DateTime createdUtc, DateTime? archivedUtc, Guid? categoryId, string? contractNumber = null, Guid? symbolAttachmentId = null)
        : this()
    {
        Id = id;
        Name = name;
        Type = type;
        TargetAmount = targetAmount;
        TargetDate = targetDate;
        IsActive = isActive;
        CreatedUtc = createdUtc;
        ArchivedUtc = archivedUtc;
        Interval = interval;
        CategoryId = categoryId;
        ContractNumber = contractNumber;
        SymbolAttachmentId = symbolAttachmentId;
    }

    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public SavingsPlanType Type { get; set; }
    public decimal? TargetAmount { get; set; }
    public DateTime? TargetDate { get; set; }
    public SavingsPlanInterval? Interval { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime? ArchivedUtc { get; set; }
    public Guid? CategoryId { get; set; }
    public string? ContractNumber { get; set; }

    // Optional symbol attachment id
    public Guid? SymbolAttachmentId { get; set; }
}

public enum SavingsPlanType
{
    OneTime,
    Recurring,
    Open
}

public enum SavingsPlanInterval
{
    Monthly,
    BiMonthly,
    Quarterly,
    SemiAnnually,
    Annually
}