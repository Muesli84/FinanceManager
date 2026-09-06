namespace FinanceManager.Domain.Securities;

/// <summary>
/// Represents a historical price (closing price) for a specific security on a given date.
/// </summary>
public sealed class SecurityPrice
{
    /// <summary>
    /// Gets the identifier of the security price record.
    /// </summary>
    /// <value>The record GUID.</value>
    public Guid Id { get; private set; } = Guid.NewGuid();

    /// <summary>
    /// Gets the identifier of the security this price belongs to.
    /// </summary>
    /// <value>The security GUID.</value>
    public Guid SecurityId { get; private set; }

    /// <summary>
    /// Gets the date of the price. Time part is ignored; stored as date component.
    /// </summary>
    /// <value>The date of the price (date component only).</value>
    public DateTime Date { get; private set; }

    /// <summary>
    /// Gets the closing price value for the security on <see cref="Date"/>.
    /// </summary>
    /// <value>Closing price.</value>
    public decimal Close { get; private set; }

    /// <summary>
    /// Gets the UTC timestamp when this price record was created.
    /// </summary>
    /// <value>Creation timestamp in UTC.</value>
    public DateTime CreatedUtc { get; private set; } = DateTime.UtcNow;

    /// <summary>
    /// Parameterless constructor for ORM/deserialization.
    /// </summary>
    private SecurityPrice() { }

    /// <summary>
    /// Creates a new <see cref="SecurityPrice"/> instance for the specified security and date.
    /// </summary>
    /// <param name="securityId">Identifier of the security. Must not be <see cref="Guid.Empty"/>.</param>
    /// <param name="date">Date of the price. The time component is ignored.</param>
    /// <param name="close">Closing price value.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="securityId"/> is <see cref="Guid.Empty"/>.</exception>
    public SecurityPrice(Guid securityId, DateTime date, decimal close)
    {
        if (securityId == Guid.Empty) throw new ArgumentException("SecurityId required", nameof(securityId));
        SecurityId = securityId;
        Date = date.Date;
        Close = close;
    }

    // Backup DTO
    /// <summary>
    /// DTO carrying the serializable state of a <see cref="SecurityPrice"/> for backup purposes.
    /// </summary>
    /// <param name="Id">Identifier of the security price record.</param>
    /// <param name="SecurityId">Identifier of the security this price belongs to.</param>
    /// <param name="Date">Date of the price (date component only).</param>
    /// <param name="Close">Closing price value for the date.</param>
    /// <param name="CreatedUtc">UTC timestamp when the price record was created.</param>
    /// <returns>The result.</returns>
    public sealed record SecurityPriceBackupDto(Guid Id, Guid SecurityId, DateTime Date, decimal Close, DateTime CreatedUtc);

    /// <summary>
    /// Creates a backup DTO for this security price record.
    /// </summary>
    /// <returns>A <see cref="SecurityPriceBackupDto"/> containing the serializable state of this price record.</returns>
    public SecurityPriceBackupDto ToBackupDto()
        => new SecurityPriceBackupDto(Id, SecurityId, Date, Close, CreatedUtc);

    /// <summary>
    /// Assigns values from a backup DTO to this entity.
    /// </summary>
    /// <param name="dto">Backup DTO to read values from.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="dto"/> is <c>null</c>.</exception>
    public void AssignBackupDto(SecurityPriceBackupDto dto)
    {
        if (dto == null) throw new ArgumentNullException(nameof(dto));
        SecurityId = dto.SecurityId;
        Date = dto.Date;
        Close = dto.Close;
        CreatedUtc = dto.CreatedUtc;
    }
}
