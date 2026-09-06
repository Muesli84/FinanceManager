namespace FinanceManager.Domain.Accounts;

/// <summary>
/// Represents a share/permission of an account granted to another user.
/// </summary>
public sealed class AccountShare : Entity
{
    /// <summary>
    /// Creates a new account share entry.
    /// </summary>
    /// <param name="accountId">Id of the shared account.</param>
    /// <param name="userId">Id of the user the account is shared with.</param>
    /// <param name="role">Role granted to the user.</param>
    public AccountShare(Guid accountId, Guid userId, AccountShareRole role)
    {
        AccountId = Guards.NotEmpty(accountId, nameof(accountId));
        UserId = Guards.NotEmpty(userId, nameof(userId));
        Role = role;
    }

    /// <summary>
    /// Identifier of the shared account.
    /// </summary>
    public Guid AccountId { get; private set; }

    /// <summary>
    /// Identifier of the user who received the share.
    /// </summary>
    public Guid UserId { get; private set; }

    /// <summary>
    /// Role granted by the share (e.g. read/write).
    /// </summary>
    public AccountShareRole Role { get; private set; }

    /// <summary>
    /// UTC timestamp when the share was granted.
    /// </summary>
    public DateTime GrantedUtc { get; private set; } = DateTime.UtcNow;

    /// <summary>
    /// UTC timestamp when the share was revoked, if applicable.
    /// </summary>
    public DateTime? RevokedUtc { get; private set; }

    // Backup DTO
    /// <summary>
    /// DTO carrying the serializable state of an <see cref="AccountShare"/> for backup purposes.
    /// </summary>
    /// <param name="Id">Entity identifier.</param>
    /// <param name="AccountId">Identifier of the shared account.</param>
    /// <param name="UserId">Identifier of the user the account is shared with.</param>
    /// <param name="Role">Granted role.</param>
    /// <param name="GrantedUtc">UTC timestamp when the share was granted.</param>
    /// <param name="RevokedUtc">UTC timestamp when the share was revoked, if any.</param>
    /// <returns>The result.</returns>
    public sealed record AccountShareBackupDto(Guid Id, Guid AccountId, Guid UserId, AccountShareRole Role, DateTime GrantedUtc, DateTime? RevokedUtc);

    /// <summary>
    /// Creates a backup DTO representing the serializable state of this account share.
    /// </summary>
    /// <returns>A <see cref="AccountShareBackupDto"/> containing values required for backup/restore.</returns>
    public AccountShareBackupDto ToBackupDto()
        => new AccountShareBackupDto(Id, AccountId, UserId, Role, GrantedUtc, RevokedUtc);

    /// <summary>
    /// Assigns values from a backup DTO to this entity instance.
    /// Uses domain setters where applicable to preserve invariants.
    /// </summary>
    /// <param name="dto">The <see cref="AccountShareBackupDto"/> containing values to apply.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="dto"/> is <c>null</c>.</exception>
    public void AssignBackupDto(AccountShareBackupDto dto)
    {
        if (dto == null) throw new ArgumentNullException(nameof(dto));
        AccountId = dto.AccountId;
        UserId = dto.UserId;
        Role = dto.Role;
        GrantedUtc = dto.GrantedUtc;
        RevokedUtc = dto.RevokedUtc;
    }
}
