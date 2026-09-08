using FinanceManager.Shared.Dtos.Budget;

namespace FinanceManager.Domain.Budget;

/// <summary>
/// A budget purpose defines what expected (planned) amounts should be tracked against.
/// A purpose aggregates planned and actual values for a specific source (contact, contact group, savings plan).
/// </summary>
public sealed class BudgetPurpose : Entity, IAggregateRoot
{
    private BudgetPurpose() { }

    /// <summary>
    /// Creates a new budget purpose owned by a user.
    /// </summary>
    /// <param name="ownerUserId">Owner user id. Must not be empty.</param>
    /// <param name="name">Name of the purpose. Must not be null or whitespace.</param>
    /// <param name="sourceType">Source type used to map actual values.</param>
    /// <param name="sourceId">Identifier of the source entity (depends on <paramref name="sourceType"/>).</param>
    /// <param name="description">Optional description.</param>
    /// <param name="valuationType">Valuation type used when computing actual values.</param>
    public BudgetPurpose(Guid ownerUserId, string name, BudgetSourceType sourceType, Guid sourceId, string? description = null, BudgetValuationType valuationType = BudgetValuationType.ExactPostings)
    {
        OwnerUserId = Guards.NotEmpty(ownerUserId, nameof(ownerUserId));
        Rename(name);
        SetSource(sourceType, sourceId);
        SetDescription(description);
        SetValuationType(valuationType);
    }

    /// <summary>
    /// Owner user identifier.
    /// </summary>
    public Guid OwnerUserId { get; private set; }

    /// <summary>
    /// Display name of the purpose.
    /// </summary>
    public string Name { get; private set; } = null!;

    /// <summary>
    /// Optional description for the purpose.
    /// </summary>
    public string? Description { get; private set; }

    /// <summary>
    /// The type of source used to resolve actual postings.
    /// </summary>
    public BudgetSourceType SourceType { get; private set; }

    /// <summary>
    /// The identifier of the source entity.
    /// </summary>
    public Guid SourceId { get; private set; }

    /// <summary>
    /// Optional category id this purpose is assigned to.
    /// </summary>
    public Guid? BudgetCategoryId { get; private set; }

    /// <summary>
    /// Determines how matching postings are valued for this purpose.
    /// </summary>
    public BudgetValuationType ValuationType { get; private set; } = BudgetValuationType.ExactPostings;

    /// <summary>
    /// Renames the purpose.
    /// </summary>
    /// <param name="name">New name.</param>
    public void Rename(string name)
    {
        Name = Guards.NotNullOrWhiteSpace(name, nameof(name));
        Touch();
    }

    /// <summary>
    /// Sets a description for the purpose.
    /// </summary>
    /// <param name="description">Description to set, or null/whitespace to clear.</param>
    public void SetDescription(string? description)
    {
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        Touch();
    }

    /// <summary>
    /// Updates the source mapping.
    /// </summary>
    /// <param name="sourceType">New source type.</param>
    /// <param name="sourceId">New source id. Must not be empty.</param>
    public void SetSource(BudgetSourceType sourceType, Guid sourceId)
    {
        SourceType = sourceType;
        SourceId = Guards.NotEmpty(sourceId, nameof(sourceId));
        Touch();
    }

    /// <summary>
    /// Assigns the purpose to a category or clears the assignment.
    /// </summary>
    /// <param name="categoryId">Category id to assign or <c>null</c> to clear.</param>
    public void SetCategory(Guid? categoryId)
    {
        BudgetCategoryId = categoryId == Guid.Empty ? null : categoryId;
        Touch();
    }

    /// <summary>
    /// Sets how matching postings are valued for this purpose.
    /// </summary>
    /// <param name="valuationType">The valuation type.</param>
    public void SetValuationType(BudgetValuationType valuationType)
    {
        ValuationType = Enum.IsDefined(valuationType) ? valuationType : BudgetValuationType.ExactPostings;
        Touch();
    }

    /// <summary>
    /// DTO carrying the serializable state of a <see cref="BudgetPurpose"/> for backup purposes.
    /// </summary>
    /// <param name="Id">Budget purpose id.</param>
    /// <param name="OwnerUserId">Owner user id.</param>
    /// <param name="Name">Purpose name.</param>
    /// <param name="Description">Optional description.</param>
    /// <param name="SourceType">Source type.</param>
    /// <param name="SourceId">Source id.</param>
    /// <param name="BudgetCategoryId">Optional category id.</param>
    /// <param name="ValuationType">Determines how matching postings are valued.</param>
    /// <returns>The result.</returns>
    public sealed record BudgetPurposeBackupDto(Guid Id, Guid OwnerUserId, string Name, string? Description, BudgetSourceType SourceType, Guid SourceId, Guid? BudgetCategoryId, BudgetValuationType ValuationType = BudgetValuationType.ExactPostings);

    /// <summary>
    /// Creates a backup DTO representing the serializable state of this budget purpose.
    /// </summary>
    /// <returns>The result.</returns>
    public BudgetPurposeBackupDto ToBackupDto()
        => new BudgetPurposeBackupDto(Id, OwnerUserId, Name, Description, SourceType, SourceId, BudgetCategoryId, ValuationType);

    /// <summary>
    /// Applies values from the provided backup DTO to this entity.
    /// </summary>
    /// <param name="dto">The dto.</param>
    public void AssignBackupDto(BudgetPurposeBackupDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);
        OwnerUserId = dto.OwnerUserId;
        Name = dto.Name;
        Description = dto.Description;
        SourceType = dto.SourceType;
        SourceId = dto.SourceId;
        BudgetCategoryId = dto.BudgetCategoryId;
        ValuationType = Enum.IsDefined(dto.ValuationType) ? dto.ValuationType : BudgetValuationType.ExactPostings;
    }
}
