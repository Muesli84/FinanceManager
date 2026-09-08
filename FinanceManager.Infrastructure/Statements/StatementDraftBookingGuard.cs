namespace FinanceManager.Infrastructure.Statements;

/// <summary>
/// Represents a durable processing guard used to prevent concurrent booking of the same statement draft.
/// </summary>
public sealed class StatementDraftBookingGuard
{
    private StatementDraftBookingGuard()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="StatementDraftBookingGuard"/> class.
    /// </summary>
    /// <param name="ownerUserId">Identifier of the draft owner.</param>
    /// <param name="draftId">Identifier of the guarded draft.</param>
    /// <param name="lockToken">Unique token identifying the active lock owner.</param>
    /// <param name="acquiredUtc">UTC timestamp when the guard was acquired.</param>
    /// <param name="expiresUtc">UTC timestamp when the guard expires.</param>
    public StatementDraftBookingGuard(Guid ownerUserId, Guid draftId, Guid lockToken, DateTime acquiredUtc, DateTime expiresUtc)
    {
        OwnerUserId = ownerUserId;
        DraftId = draftId;
        LockToken = lockToken;
        AcquiredUtc = acquiredUtc;
        ExpiresUtc = expiresUtc;
    }

    /// <summary>
    /// Gets or sets the unique identifier of the guard row.
    /// </summary>
    /// <returns>The result.</returns>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Gets or sets the owner user identifier.
    /// </summary>
    public Guid OwnerUserId { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the guarded statement draft.
    /// </summary>
    public Guid DraftId { get; set; }

    /// <summary>
    /// Gets or sets the unique token for the active guard holder.
    /// </summary>
    public Guid LockToken { get; set; }

    /// <summary>
    /// Gets or sets the UTC timestamp when the guard was acquired.
    /// </summary>
    public DateTime AcquiredUtc { get; set; }

    /// <summary>
    /// Gets or sets the UTC timestamp when the guard expires.
    /// </summary>
    public DateTime ExpiresUtc { get; set; }
}
