using FinanceManager.Shared.Dtos.Budget;

namespace FinanceManager.Domain.Budget;

/// <summary>
/// Overrides allow replacing the calculated planned amount for a specific period.
/// Overrides are applied with higher priority than rules.
/// </summary>
public sealed class BudgetOverride : Entity, IAggregateRoot
{
    private BudgetOverride() { }

    /// <summary>
    /// Creates a new budget override.
    /// </summary>
    /// <param name="ownerUserId">Owner user identifier.</param>
    /// <param name="budgetPurposeId">Budget purpose identifier.</param>
    /// <param name="period">Target period to override (monthly).</param>
    /// <param name="amount">Replacement amount.</param>
    public BudgetOverride(Guid ownerUserId, Guid budgetPurposeId, BudgetPeriodKey period, decimal amount)
    {
        OwnerUserId = Guards.NotEmpty(ownerUserId, nameof(ownerUserId));
        BudgetPurposeId = Guards.NotEmpty(budgetPurposeId, nameof(budgetPurposeId));
        SetPeriod(period);
        Amount = amount;
    }

    /// <summary>
    /// Owner user identifier.
    /// </summary>
    public Guid OwnerUserId { get; private set; }

    /// <summary>
    /// Budget purpose identifier.
    /// </summary>
    public Guid BudgetPurposeId { get; private set; }

    /// <summary>
    /// Override period year.
    /// </summary>
    public int PeriodYear { get; private set; }

    /// <summary>
    /// Override period month (1..12).
    /// </summary>
    public int PeriodMonth { get; private set; }

    /// <summary>
    /// Target period to override.
    /// </summary>
    /// <returns>The result.</returns>
    public BudgetPeriodKey Period
    {
        get
        {
            return new BudgetPeriodKey(PeriodYear, PeriodMonth);
        }
    }

    /// <summary>
    /// Replacement planned amount for the period.
    /// </summary>
    public decimal Amount { get; private set; }

    /// <summary>
    /// Updates the override amount.
    /// </summary>
    /// <param name="amount">New amount.</param>
    public void SetAmount(decimal amount)
    {
        Amount = amount;
        Touch();
    }

    /// <summary>
    /// Updates the override period.
    /// </summary>
    /// <param name="period">New period.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when period month is invalid.</exception>
    public void SetPeriod(BudgetPeriodKey period)
    {
        period.Validate();
        PeriodYear = period.Year;
        PeriodMonth = period.Month;
        Touch();
    }

    /// <summary>
    /// DTO carrying the serializable state of a <see cref="BudgetOverride"/> for backup purposes.
    /// </summary>
    /// <param name="Id">Override id.</param>
    /// <param name="OwnerUserId">Owner user id.</param>
    /// <param name="BudgetPurposeId">Budget purpose id.</param>
    /// <param name="PeriodYear">Period year.</param>
    /// <param name="PeriodMonth">Period month.</param>
    /// <param name="Amount">Override amount.</param>
    /// <returns>The result.</returns>
    public sealed record BudgetOverrideBackupDto(Guid Id, Guid OwnerUserId, Guid BudgetPurposeId, int PeriodYear, int PeriodMonth, decimal Amount);

    /// <summary>
    /// Creates a backup DTO representing the serializable state of this budget override.
    /// </summary>
    /// <returns>The result.</returns>
    public BudgetOverrideBackupDto ToBackupDto()
        => new BudgetOverrideBackupDto(Id, OwnerUserId, BudgetPurposeId, PeriodYear, PeriodMonth, Amount);

    /// <summary>
    /// Applies values from the provided backup DTO to this entity.
    /// </summary>
    /// <param name="dto">The dto.</param>
    public void AssignBackupDto(BudgetOverrideBackupDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);
        OwnerUserId = dto.OwnerUserId;
        BudgetPurposeId = dto.BudgetPurposeId;
        PeriodYear = dto.PeriodYear;
        PeriodMonth = dto.PeriodMonth;
        Amount = dto.Amount;
    }
}
