namespace FinanceManager.Domain.Securities;

/// <summary>
/// Represents a financial security (e.g. stock, bond) tracked by a user. Contains metadata, status and optional price error state.
/// </summary>
public sealed class Security : Entity
{
    /// <summary>
    /// Parameterless constructor for ORM/deserialization.
    /// </summary>
    private Security() { }

    /// <summary>
    /// Creates a new <see cref="Security"/> instance for the given owner.
    /// </summary>
    /// <param name="ownerUserId">Identifier of the user who owns the security.</param>
    /// <param name="name">Display name of the security. Must not be null or whitespace.</param>
    /// <param name="identifier">Identifier such as WKN or ISIN. Must not be null or whitespace.</param>
    /// <param name="description">Optional description.</param>
    /// <param name="alphaVantageCode">Optional AlphaVantage symbol/code used for price lookups.</param>
    /// <param name="currencyCode">Currency ISO code (e.g. "EUR"). Must not be null or whitespace.</param>
    /// <param name="categoryId">Optional category id.</param>
    /// <param name="region">Optional region of the security (e.g. "Europa", "Nordamerika") used for regional distribution.</param>
    /// <param name="sector">Optional sector of the security (e.g. "Technologie", "Pharma") used for sector distribution.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="name"/>, <paramref name="identifier"/> or <paramref name="currencyCode"/> are null or whitespace. See <see cref="Update"/>.</exception>
    public Security(Guid ownerUserId, string name, string identifier, string? description, string? alphaVantageCode, string currencyCode, Guid? categoryId, string? region = null, string? sector = null)
    {
        Id = Guid.NewGuid();
        OwnerUserId = ownerUserId;
        Update(name, identifier, description, alphaVantageCode, currencyCode, categoryId, region, sector);
        CreatedUtc = DateTime.UtcNow;
        IsActive = true;
    }

    /// <summary>
    /// Gets the owner user identifier of the security.
    /// </summary>
    /// <value>The owner's GUID.</value>
    public Guid OwnerUserId { get; private set; }

    /// <summary>
    /// Gets the display name of the security.
    /// </summary>
    /// <value>The name string.</value>
    public string Name { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the optional description of the security.
    /// </summary>
    /// <value>Description or <c>null</c>.</value>
    public string? Description { get; private set; }

    /// <summary>
    /// Gets the primary identifier of the security (e.g. WKN or ISIN).
    /// </summary>
    /// <value>Identifier string.</value>
    public string Identifier { get; private set; } = string.Empty; // WKN / ISIN

    /// <summary>
    /// Optional code used with AlphaVantage or other external price providers.
    /// </summary>
    /// <value>AlphaVantage symbol or <c>null</c>.</value>
    public string? AlphaVantageCode { get; private set; }

    /// <summary>
    /// ISO currency code used for prices (uppercase).
    /// </summary>
    /// <value>Currency code, e.g. "EUR".</value>
    public string CurrencyCode { get; private set; } = "EUR";

    /// <summary>
    /// Optional category identifier for this security.
    /// </summary>
    /// <value>Category GUID or <c>null</c>.</value>
    public Guid? CategoryId { get; private set; }          // NEW

    /// <summary>
    /// Optional region of the security (e.g. "Europa", "Nordamerika") used for regional distribution in the portfolio analysis report.
    /// </summary>
    /// <value>Region string or <c>null</c>.</value>
    public string? Region { get; private set; }

    /// <summary>
    /// Optional sector of the security (e.g. "Technologie", "Pharma") used for sector distribution in the portfolio analysis report.
    /// </summary>
    /// <value>Sector string or <c>null</c>.</value>
    public string? Sector { get; private set; }

    /// <summary>
    /// Indicates whether the security is active.
    /// </summary>
    /// <value><c>true</c> when active; otherwise <c>false</c>.</value>
    public bool IsActive { get; private set; }

    /// <summary>
    /// UTC timestamp when the security was archived, or <c>null</c> if still active.
    /// </summary>
    /// <value>Archive time in UTC or <c>null</c>.</value>
    public DateTime? ArchivedUtc { get; private set; }

    /// <summary>
    /// Indicates that there was an error fetching prices for this security.
    /// </summary>
    /// <value><c>true</c> if a price error is active; otherwise <c>false</c>.</value>
    public bool HasPriceError { get; private set; }

    /// <summary>
    /// Optional human-readable price error message.
    /// </summary>
    /// <value>Error message or <c>null</c>.</value>
    public string? PriceErrorMessage { get; private set; }

    /// <summary>
    /// UTC timestamp since when the price error state was active, or <c>null</c> if none.
    /// </summary>
    /// <value>UTC time or <c>null</c>.</value>
    public DateTime? PriceErrorSinceUtc { get; private set; }

    /// <summary>
    /// Optional reference to a symbol attachment.
    /// </summary>
    /// <value>Attachment GUID or <c>null</c>.</value>
    public Guid? SymbolAttachmentId { get; private set; }

    /// <summary>
    /// Updates core metadata for the security.
    /// </summary>
    /// <param name="name">Display name. Must not be null or whitespace.</param>
    /// <param name="identifier">Primary identifier (WKN/ISIN). Must not be null or whitespace.</param>
    /// <param name="description">Optional description.</param>
    /// <param name="alphaVantageCode">Optional external provider code.</param>
    /// <param name="currencyCode">ISO currency code. Must not be null or whitespace.</param>
    /// <param name="categoryId">Optional category id.</param>
    /// <param name="region">Optional region of the security (e.g. "Europa", "Nordamerika"). Max. 255 characters.</param>
    /// <param name="sector">Optional sector of the security (e.g. "Technologie", "Pharma"). Max. 255 characters.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="name"/>, <paramref name="identifier"/>, or <paramref name="currencyCode"/> are null or whitespace, or when <paramref name="region"/> or <paramref name="sector"/> exceed 255 characters.</exception>
    public void Update(string name, string identifier, string? description, string? alphaVantageCode, string currencyCode, Guid? categoryId, string? region = null, string? sector = null)
    {
        if (string.IsNullOrWhiteSpace(name)) { throw new ArgumentException("Name required", nameof(name)); }
        if (string.IsNullOrWhiteSpace(identifier)) { throw new ArgumentException("Identifier required", nameof(identifier)); }
        if (string.IsNullOrWhiteSpace(currencyCode)) { throw new ArgumentException("Currency required", nameof(currencyCode)); }
        if (region != null && region.Trim().Length > 255) { throw new ArgumentException("Region must not exceed 255 characters", nameof(region)); }
        if (sector != null && sector.Trim().Length > 255) { throw new ArgumentException("Sector must not exceed 255 characters", nameof(sector)); }

        Name = name.Trim();
        Identifier = identifier.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        AlphaVantageCode = string.IsNullOrWhiteSpace(alphaVantageCode) ? null : alphaVantageCode.Trim();
        CurrencyCode = currencyCode.Trim().ToUpperInvariant();
        CategoryId = categoryId;
        Region = string.IsNullOrWhiteSpace(region) ? null : region.Trim();
        Sector = string.IsNullOrWhiteSpace(sector) ? null : sector.Trim();
    }

    /// <summary>
    /// Archives the security if it is currently active. No-op when already archived.
    /// </summary>
    public void Archive()
    {
        if (!IsActive) { return; }
        IsActive = false;
        ArchivedUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Marks the security as having a price fetch error and records the message and timestamp.
    /// </summary>
    /// <param name="message">Error message to record. Whitespace-only strings are preserved as provided; if null or whitespace, a default message will be stored by the implementation.</param>
    public void SetPriceError(string message)
    {
        HasPriceError = true;
        PriceErrorMessage = string.IsNullOrWhiteSpace(message) ? "Unknown error" : message;
        PriceErrorSinceUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Clears the price error state and associated metadata.
    /// </summary>
    public void ClearPriceError()
    {
        HasPriceError = false;
        PriceErrorMessage = null;
        PriceErrorSinceUtc = null;
    }

    /// <summary>
    /// Sets or clears the symbol attachment reference. Passing <see cref="Guid.Empty"/> is treated as <c>null</c>.
    /// </summary>
    /// <param name="attachmentId">Attachment GUID to set, or <see cref="Guid.Empty"/>/<c>null</c> to clear.</param>
    public void SetSymbolAttachment(Guid? attachmentId)
    {
        SymbolAttachmentId = attachmentId == Guid.Empty ? null : attachmentId;
    }
    /// <summary>
    /// Sets the creation, modification, and archival dates for the current instance.
    /// </summary>
    /// <param name="createdUtc">The date and time, in UTC, when the instance was created.</param>
    /// <param name="modifiedUtc">The date and time, in UTC, when the instance was last modified, or <see langword="null"/> if the modification
    /// date is not set.</param>
    /// <param name="archivedUtc">The date and time, in UTC, when the instance was archived, or <see langword="null"/> if the archival date is not
    /// set.</param>
    private void SetDates(DateTime createdUtc, DateTime? modifiedUtc, DateTime? archivedUtc)
    {
        SetDates(createdUtc, modifiedUtc);
        ArchivedUtc = archivedUtc;
    }

    // Backup DTO
    /// <summary>
    /// DTO carrying the serializable state of a <see cref="Security"/> for backup purposes.
    /// </summary>
    /// <param name="Id">Identifier of the security entity.</param>
    /// <param name="OwnerUserId">Identifier of the user who owns the security.</param>
    /// <param name="Name">Display name of the security.</param>
    /// <param name="Identifier">Primary identifier (e.g. WKN or ISIN).</param>
    /// <param name="Description">Optional description text.</param>
    /// <param name="AlphaVantageCode">Optional code for external price providers.</param>
    /// <param name="CurrencyCode">ISO currency code used for prices.</param>
    /// <param name="CategoryId">Optional category identifier for the security.</param>
    /// <param name="IsActive">Whether the security is active.</param>
    /// <param name="CreatedUtc">Creation timestamp in UTC.</param>
    /// <param name="ModifiedUtc">Last modification timestamp in UTC, if any.</param>
    /// <param name="ArchivedUtc">Archive timestamp in UTC if archived.</param>
    /// <param name="SymbolAttachmentId">Optional symbol attachment identifier.</param>
    /// <param name="Region">Optional region of the security.</param>
    /// <param name="Sector">Optional sector of the security.</param>
    /// <returns>A backup record instance.</returns>
    public sealed record SecurityBackupDto(Guid Id, Guid OwnerUserId, string Name, string Identifier, string? Description, string? AlphaVantageCode, string CurrencyCode, Guid? CategoryId, bool IsActive, DateTime CreatedUtc, DateTime? ModifiedUtc, DateTime? ArchivedUtc, Guid? SymbolAttachmentId, string? Region = null, string? Sector = null);

    /// <summary>
    /// Creates a backup DTO representing this security.
    /// </summary>
    /// <param name="OwnerUserId">The owner user id.</param>
    /// <param name="Name">The name.</param>
    /// <param name="Identifier">The identifier.</param>
    /// <param name="Description">The description.</param>
    /// <param name="AlphaVantageCode">The alpha vantage code.</param>
    /// <param name="CurrencyCode">The currency code.</param>
    /// <param name="CategoryId">The category id.</param>
    /// <param name="IsActive">The is active.</param>
    /// <param name="CreatedUtc">The created utc.</param>
    /// <param name="ModifiedUtc">The modified utc.</param>
    /// <param name="ArchivedUtc">The archived utc.</param>
    /// <param name="SymbolAttachmentId">The symbol attachment id.</param>
    /// <param name="Region">The region.</param>
    /// <param name="Sector">The sector.</param>
    /// <returns>A <see cref="SecurityBackupDto"/> with serializable security data.</returns>
    public SecurityBackupDto ToBackupDto() => new SecurityBackupDto(Id, OwnerUserId, Name, Identifier, Description, AlphaVantageCode, CurrencyCode, CategoryId, IsActive, CreatedUtc, ModifiedUtc, ArchivedUtc, SymbolAttachmentId, Region, Sector);

    /// <summary>
    /// Assigns values from a backup DTO to this entity.
    /// </summary>
    /// <param name="dto">Backup DTO to apply.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="dto"/> is <c>null</c>.</exception>
    public void AssignBackupDto(SecurityBackupDto dto)
    {
        if (dto == null) throw new ArgumentNullException(nameof(dto));
        Update(dto.Name, dto.Identifier, dto.Description, dto.AlphaVantageCode, dto.CurrencyCode, dto.CategoryId, dto.Region, dto.Sector);
        if (!dto.IsActive && IsActive) Archive();
        SymbolAttachmentId = dto.SymbolAttachmentId;
        SetDates(dto.CreatedUtc, dto.ModifiedUtc, dto.ArchivedUtc);
    }
}
