namespace FinanceManager.Domain.Contacts;

/// <summary>
/// Represents an alias pattern for a contact used to match imported postings.
/// </summary>
public sealed class AliasName : Entity
{
    /// <summary>
    /// Creates a new alias pattern for the specified contact.
    /// </summary>
    /// <param name="contactId">Contact identifier the alias belongs to.</param>
    /// <param name="pattern">Alias pattern string.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="contactId"/> is an empty GUID or <paramref name="pattern"/> is null/whitespace.</exception>
    public AliasName(Guid contactId, string pattern)
    {
        ContactId = Guards.NotEmpty(contactId, nameof(contactId));
        Pattern = Guards.NotNullOrWhiteSpace(pattern, nameof(pattern));
    }

    /// <summary>
    /// Identifier of the contact this alias is associated with.
    /// </summary>
    /// <value>The contact GUID.</value>
    public Guid ContactId { get; private set; }

    /// <summary>
    /// Alias pattern used for matching (e.g. contains or regex pattern).
    /// </summary>
    /// <value>The pattern string used to match postings.</value>
    public string Pattern { get; private set; } = null!;

    /// <summary>
    /// Reassigns this alias to a different contact.
    /// </summary>
    /// <param name="newContactId">Target contact identifier.</param>
    public void ReassignTo(Guid newContactId)
    {
        ContactId = newContactId;
    }

    /// <summary>
    /// Sets a new pattern for the alias.
    /// </summary>
    /// <param name="pattern">The new pattern to be set.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="pattern"/> is null, empty or whitespace.</exception>
    public void SetPattern(string pattern)
    {
        Pattern = Guards.NotNullOrWhiteSpace(pattern, nameof(pattern));
        Touch();
    }

    // Backup DTO
    /// <summary>
    /// DTO carrying the serializable state of an <see cref="AliasName"/> for backup purposes.
    /// </summary>
    /// <param name="Id">Identifier of the alias entity.</param>
    /// <param name="ContactId">Identifier of the contact this alias belongs to.</param>
    /// <param name="Pattern">Alias pattern string.</param>
    /// <param name="CreatedUtc">Creation timestamp in UTC.</param>
    /// <param name="ModifiedUtc">Last modification timestamp in UTC, if any.</param>
    /// <returns>The result.</returns>
    public sealed record AliasNameBackupDto(Guid Id, Guid ContactId, string Pattern, DateTime CreatedUtc, DateTime? ModifiedUtc);

    /// <summary>
    /// Converts the current alias name to its backup DTO representation.
    /// </summary>
    /// <returns>A <see cref="AliasNameBackupDto"/> containing the alias name data.</returns>
    public AliasNameBackupDto ToBackupDto()
    {
        return new AliasNameBackupDto(Id, ContactId, Pattern, CreatedUtc, ModifiedUtc);
    }

    /// <summary>
    /// Assigns the data from a backup DTO to the current alias name.
    /// </summary>
    /// <param name="dto">The backup DTO containing the data to be assigned.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="dto"/> is <c>null</c>.</exception>
    public void AssignBackupDto(AliasNameBackupDto dto)
    {
        if (dto == null) throw new ArgumentNullException(nameof(dto));
        ContactId = dto.ContactId;
        SetPattern(dto.Pattern);
        SetDates(dto.CreatedUtc, dto.ModifiedUtc);
    }
}