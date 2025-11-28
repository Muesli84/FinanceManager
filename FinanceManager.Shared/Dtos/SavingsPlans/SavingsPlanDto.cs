namespace FinanceManager.Shared.Dtos.SavingsPlans;

/// <summary>
/// DTO describing a savings plan including target and interval configuration.
/// </summary>
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

    /// <summary>Unique savings plan identifier.</summary>
    public Guid Id { get; set; }
    /// <summary>Display name of the plan.</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Type of the plan.</summary>
    public SavingsPlanType Type { get; set; }
    /// <summary>Optional target amount.</summary>
    public decimal? TargetAmount { get; set; }
    /// <summary>Optional target date.</summary>
    public DateTime? TargetDate { get; set; }
    /// <summary>Optional recurrence interval.</summary>
    public SavingsPlanInterval? Interval { get; set; }
    /// <summary>Indicates whether the plan is currently active.</summary>
    public bool IsActive { get; set; }
    /// <summary>UTC timestamp when the plan was created.</summary>
    public DateTime CreatedUtc { get; set; }
    /// <summary>UTC timestamp when the plan was archived, if any.</summary>
    public DateTime? ArchivedUtc { get; set; }
    /// <summary>Optional category id the plan belongs to.</summary>
    public Guid? CategoryId { get; set; }
    /// <summary>Optional contract number associated with the plan.</summary>
    public string? ContractNumber { get; set; }

    /// <summary>Optional symbol attachment id.</summary>
    public Guid? SymbolAttachmentId { get; set; }
}
