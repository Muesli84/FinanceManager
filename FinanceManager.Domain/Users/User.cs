using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations.Schema;

namespace FinanceManager.Domain.Users;

/// <summary>
/// Domain user entity extending ASP.NET Identity's <see cref="IdentityUser{Guid}"/> with application-specific preferences and settings.
/// </summary>
public sealed partial class User : IdentityUser<Guid>, IAggregateRoot
{
    /// <summary>
    /// Parameterless constructor for ORM/deserialization.
    /// </summary>
    private User() { }

    /// <summary>
    /// Creates a user with a pre-computed password hash. Use this constructor when the caller already computed the password hash.
    /// </summary>
    /// <param name="username">The username. Must not be null or whitespace.</param>
    /// <param name="passwordHash">The pre-computed password hash. Must not be null or whitespace.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="username"/> or <paramref name="passwordHash"/> are null or whitespace (via guards).</exception>
    public User(string username, string passwordHash)
    {
        Rename(username);
        SetPasswordHash(passwordHash);
        // Defaults for import split settings (FA-AUSZ-016)
        ImportSplitMode = ImportSplitMode.MonthlyOrFixed;
        ImportMaxEntriesPerDraft = 250;
        ImportMonthlySplitThreshold = 250; // equals Max by default
        ImportMinEntriesPerDraft = 8; // new default (FA-AUSZ-016-12)
        MassImportDialogPolicy = MassImportDialogPolicy.OnMissingInformation;
    }

    /// <summary>
    /// Backwards-compatible alias used in other parts of the codebase. Use <c>UserName</c> from the Identity base class instead.
    /// This property is not mapped to the database and is obsolete.
    /// </summary>
    [NotMapped]
    [Obsolete("Use UserName property from IdentityUser base class instead.", true)]
    public string Username { get => base.UserName!; set => base.UserName = value; }

    /// <summary>
    /// Creates a user instance intended for use with ASP.NET Identity's <c>UserManager.CreateAsync(user, password)</c> overload.
    /// Password will be set by the <c>UserManager</c> and not by this constructor.
    /// </summary>
    /// <param name="username">The username. Must not be null or whitespace.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="username"/> is null or whitespace (via guards).</exception>
    public User(string username)
    {
        UserName = Guards.NotNullOrWhiteSpace(username, nameof(username));
        // Password will be set by Identity's UserManager when CreateAsync(user, password) is used
        // Defaults for import split settings
        ImportSplitMode = ImportSplitMode.MonthlyOrFixed;
        ImportMaxEntriesPerDraft = 250;
        ImportMonthlySplitThreshold = 250;
        ImportMinEntriesPerDraft = 8;
        MassImportDialogPolicy = MassImportDialogPolicy.OnMissingInformation;
    }

    /// <summary>
    /// Creates a new user and sets the admin flag.
    /// </summary>
    /// <param name="username">The username. Must not be null or whitespace.</param>
    /// <param name="isAdmin">Whether the created user should be an administrator.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="username"/> is null or whitespace (via guards).</exception>
    public User(string username, bool isAdmin)
    {
        UserName = Guards.NotNullOrWhiteSpace(username, nameof(username));
        IsAdmin = isAdmin;
        // Defaults for import split settings
        ImportSplitMode = ImportSplitMode.MonthlyOrFixed;
        ImportMaxEntriesPerDraft = 250;
        ImportMonthlySplitThreshold = 250;
        ImportMinEntriesPerDraft = 8;
    }

    /// <summary>
    /// Creates a new user providing a pre-computed password hash and admin flag (backwards-compatible overload).
    /// </summary>
    /// <param name="username">The username. Must not be null or whitespace.</param>
    /// <param name="passwordHash">Pre-computed password hash. Must not be null or whitespace.</param>
    /// <param name="isAdmin">Whether the user should be an administrator.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="username"/> or <paramref name="passwordHash"/> are null or whitespace (via guards).</exception>
    public User(string username, string passwordHash, bool isAdmin)
    {
        UserName = Guards.NotNullOrWhiteSpace(username, nameof(username));
        base.PasswordHash = Guards.NotNullOrWhiteSpace(passwordHash, nameof(passwordHash));
        IsAdmin = isAdmin;
        // Defaults for import split settings
        ImportSplitMode = ImportSplitMode.MonthlyOrFixed;
        ImportMaxEntriesPerDraft = 250;
        ImportMonthlySplitThreshold = 250;
        ImportMinEntriesPerDraft = 8;
    }

    /// <summary>
    /// Preferred UI language tag for the user (e.g. "en", "de").
    /// </summary>
    /// <value>Language code or <c>null</c> if not set.</value>
    public string? PreferredLanguage { get; private set; }

    /// <summary>
    /// UTC timestamp of the user's last successful login.
    /// </summary>
    /// <value>Last login time in UTC.</value>
    public DateTime LastLoginUtc { get; private set; }

    /// <summary>
    /// Whether the user account is active. Deactivated users cannot authenticate.
    /// </summary>
    /// <value><c>true</c> when active; otherwise <c>false</c>.</value>
    public bool Active { get; private set; } = true;

    // --- Import Split Settings (User Preferences) ---

    /// <summary>
    /// Mode that controls how statement imports are split into drafts.
    /// </summary>
    /// <value>The import split mode.</value>
    public ImportSplitMode ImportSplitMode { get; private set; } = ImportSplitMode.MonthlyOrFixed;

    /// <summary>
    /// Maximum number of entries allowed per generated draft during splitting.
    /// </summary>
    /// <value>Maximum entries per draft.</value>
    public int ImportMaxEntriesPerDraft { get; private set; } = 250;

    /// <summary>
    /// Threshold used in MonthlyOrFixed mode that controls when monthly split is applied. Nullable to allow fallback behavior.
    /// </summary>
    /// <value>Threshold value or <c>null</c>.</value>
    public int? ImportMonthlySplitThreshold { get; private set; } = 250; // nullable to allow future unset -> fallback

    /// <summary>
    /// Minimum number of entries per draft when splitting imports.
    /// </summary>
    /// <value>Minimum entries per draft.</value>
    public int ImportMinEntriesPerDraft { get; private set; } = 1; // new minimum entries preference

    /// <summary>
    /// Dialog behavior policy for mixed start-page mass imports.
    /// </summary>
    public MassImportDialogPolicy MassImportDialogPolicy { get; private set; } = MassImportDialogPolicy.OnMissingInformation;

    /// <summary>
    /// Whether known contacts from the application catalog may be created automatically during statement classification.
    /// </summary>
    public bool KnownContactAutoCreateEnabled { get; private set; } = true;

    /// <summary>
    /// Whether the user has enabled caching of home page KPI data in the browser's local storage.
    /// </summary>
    public bool CacheKpisInLocalStorage { get; private set; }

    /// <summary>
    /// Whether confirmation dialogs should be shown before destructive or irreversible actions.
    /// </summary>
    /// <value><c>true</c> when confirmation dialogs are enabled; otherwise <c>false</c>.</value>
    public bool ShowConfirmations { get; private set; } = true;

    /// <summary>
    /// Admin flag persisted in the database.
    /// </summary>
    /// <value><c>true</c> for administrators.</value>
    public bool IsAdmin { get; private set; }

    /// <summary>
    /// Optional reference to a user symbol attachment.
    /// </summary>
    /// <value>Attachment GUID or <c>null</c>.</value>
    public Guid? SymbolAttachmentId { get; private set; }

    private void Touch() { /* marker for state change � intentionally no-op for now */ }

    /// <summary>
    /// Sets or clears the administrator flag for the user.
    /// </summary>
    /// <param name="isAdmin">True to mark as admin; false to revoke admin rights.</param>
    public void SetAdmin(bool isAdmin)
    {
        IsAdmin = isAdmin;
        Touch();
    }

    /// <summary>
    /// Sets import split settings for the user.
    /// </summary>
    /// <param name="mode">Split mode to apply.</param>
    /// <param name="maxEntriesPerDraft">Maximum entries per draft. Must be >= 1.</param>
    /// <param name="monthlySplitThreshold">Monthly split threshold; semantics depend on <paramref name="mode"/>.</param>
    /// <param name="minEntriesPerDraft">Optional minimum entries per draft. When provided it must be &gt;= 1 and &lt;= <paramref name="maxEntriesPerDraft"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when numeric parameters are outside allowed ranges, or when monthly threshold constraints are violated.</exception>
    public void SetImportSplitSettings(ImportSplitMode mode, int maxEntriesPerDraft, int? monthlySplitThreshold, int? minEntriesPerDraft = null)
    {
        if (maxEntriesPerDraft < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxEntriesPerDraft), "Max entries per draft must be >= 1.");
        }
        if (minEntriesPerDraft.HasValue)
        {
            if (minEntriesPerDraft.Value < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(minEntriesPerDraft), "Min entries per draft must be >= 1.");
            }
            if (minEntriesPerDraft.Value > maxEntriesPerDraft)
            {
                throw new ArgumentOutOfRangeException(nameof(minEntriesPerDraft), "Min entries must be <= Max entries per draft.");
            }
            ImportMinEntriesPerDraft = minEntriesPerDraft.Value;
        }

        if (mode == ImportSplitMode.MonthlyOrFixed)
        {
            var thr = monthlySplitThreshold ?? maxEntriesPerDraft;
            if (thr < maxEntriesPerDraft)
            {
                throw new ArgumentOutOfRangeException(nameof(monthlySplitThreshold), "Monthly split threshold must be >= MaxEntriesPerDraft in MonthlyOrFixed mode.");
            }
            ImportMonthlySplitThreshold = thr;
        }
        else
        {
            // For FixedSize / Monthly the threshold is not required; keep previous value for potential later switch.
            if (monthlySplitThreshold.HasValue && monthlySplitThreshold.Value < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(monthlySplitThreshold));
            }
            if (monthlySplitThreshold.HasValue)
            {
                ImportMonthlySplitThreshold = monthlySplitThreshold.Value; // store if provided
            }
        }

        ImportSplitMode = mode;
        ImportMaxEntriesPerDraft = maxEntriesPerDraft;
        Touch();
    }

    /// <summary>
    /// Sets the dialog behavior policy for start-page mass imports.
    /// </summary>
    /// <param name="policy">Dialog policy to persist for the user.</param>
    public void SetMassImportDialogPolicy(MassImportDialogPolicy policy)
    {
        MassImportDialogPolicy = policy;
        Touch();
    }

    /// <summary>
    /// Enables or disables automatic creation of known contacts during statement classification.
    /// </summary>
    /// <param name="enabled">True to allow automatic creation; false to disable it.</param>
    public void SetKnownContactAutoCreateEnabled(bool enabled)
    {
        KnownContactAutoCreateEnabled = enabled;
        Touch();
    }

    /// <summary>
    /// Enables or disables caching of home page KPI data in the browser's local storage.
    /// </summary>
    /// <param name="enabled">True to enable local storage KPI caching; false to disable it.</param>
    public void SetCacheKpisInLocalStorage(bool enabled)
    {
        CacheKpisInLocalStorage = enabled;
        Touch();
    }

    /// <summary>
    /// Enables or disables confirmation dialogs before destructive or irreversible actions.
    /// </summary>
    /// <param name="enabled">True to show confirmation dialogs; false to suppress them.</param>
    public void SetShowConfirmations(bool enabled)
    {
        ShowConfirmations = enabled;
        Touch();
    }

    /// <summary>
    /// Records the last login time for the user.
    /// </summary>
    /// <param name="utcNow">UTC time of the login event.</param>
    public void MarkLogin(DateTime utcNow)
    {
        LastLoginUtc = utcNow;
    }

    /// <summary>
    /// Sets the preferred UI language for the user.
    /// </summary>
    /// <param name="lang">Language tag or null to clear the preference.</param>
    public void SetPreferredLanguage(string? lang) => PreferredLanguage = string.IsNullOrWhiteSpace(lang) ? null : lang.Trim();

    /// <summary>
    /// Deactivates the user account (prevents authentication).
    /// </summary>
    public void Deactivate() => Active = false;

    /// <summary>
    /// Activates the user account.
    /// </summary>
    public void Activate() => Active = true;

    /// <summary>
    /// Changes the user's username.
    /// </summary>
    /// <param name="newUsername">The new username. Must not be null or whitespace.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="newUsername"/> is null or whitespace (via guards).</exception>
    public void Rename(string newUsername) => base.UserName = Guards.NotNullOrWhiteSpace(newUsername, nameof(newUsername));

    /// <summary>
    /// Sets the user's password hash. Use Identity's password management APIs when possible.
    /// </summary>
    /// <param name="passwordHash">Precomputed password hash. Must not be null or whitespace.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="passwordHash"/> is null or whitespace (via guards).</exception>
    public void SetPasswordHash(string passwordHash) => base.PasswordHash = Guards.NotNullOrWhiteSpace(passwordHash, nameof(passwordHash));

    /// <summary>
    /// Sets or clears the user's symbol attachment. Passing <see cref="Guid.Empty"/> is treated as clearing the attachment.
    /// </summary>
    /// <param name="attachmentId">Attachment GUID to set, or <see cref="Guid.Empty"/>/<c>null</c> to clear.</param>
    public void SetSymbolAttachment(Guid? attachmentId)
    {
        SymbolAttachmentId = attachmentId == Guid.Empty ? null : attachmentId;
        Touch();
    }
}
