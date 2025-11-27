using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations.Schema;

namespace FinanceManager.Domain.Users;

public sealed partial class User : IdentityUser<Guid>, IAggregateRoot
{
    private User() { }

    // Existing constructor for callers that already compute a password hash
    public User(string username, string passwordHash)
    {
        Rename(username);
        SetPasswordHash(passwordHash);
        // Defaults for import split settings (FA-AUSZ-016)
        ImportSplitMode = ImportSplitMode.MonthlyOrFixed;
        ImportMaxEntriesPerDraft = 250;
        ImportMonthlySplitThreshold = 250; // equals Max by default
        ImportMinEntriesPerDraft = 8; // new default (FA-AUSZ-016-12)
    }

    // Backwards-compatible alias used in other parts of the codebase
    [NotMapped]
    [Obsolete("Use UserName property from IdentityUser base class instead.", true)]
    public string Username { get => base.UserName!; set => base.UserName = value; }

    // New constructor to support UserManager.CreateAsync(user, password)
    public User(string username)
    {
        UserName = Guards.NotNullOrWhiteSpace(username, nameof(username));
        // Password will be set by Identity's UserManager when CreateAsync(user, password) is used
        // Defaults for import split settings
        ImportSplitMode = ImportSplitMode.MonthlyOrFixed;
        ImportMaxEntriesPerDraft = 250;
        ImportMonthlySplitThreshold = 250;
        ImportMinEntriesPerDraft = 8;
    }

    // New constructor to support creating a user and specifying admin flag
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

    // New constructor to preserve older callsites that passed isAdmin flag
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

    public string? PreferredLanguage { get; private set; }
    public DateTime LastLoginUtc { get; private set; }
    public bool Active { get; private set; } = true;

    // --- Import Split Settings (User Preferences) ---
    public ImportSplitMode ImportSplitMode { get; private set; } = ImportSplitMode.MonthlyOrFixed;
    public int ImportMaxEntriesPerDraft { get; private set; } = 250;
    public int? ImportMonthlySplitThreshold { get; private set; } = 250; // nullable to allow future unset -> fallback
    public int ImportMinEntriesPerDraft { get; private set; } = 1; // new minimum entries preference

    // Admin flag persisted in DB
    public bool IsAdmin { get; private set; }

    // Optional user symbol
    public Guid? SymbolAttachmentId { get; private set; }

    private void Touch() { /* marker for state change — intentionally no-op for now */ }

    public void SetAdmin(bool isAdmin)
    {
        IsAdmin = isAdmin;
        Touch();
    }

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

    public void MarkLogin(DateTime utcNow)
    {
        LastLoginUtc = utcNow;
    }

    public void SetPreferredLanguage(string? lang) => PreferredLanguage = string.IsNullOrWhiteSpace(lang) ? null : lang.Trim();
    public void Deactivate() => Active = false;
    public void Activate() => Active = true;
    public void Rename(string newUsername) => base.UserName = Guards.NotNullOrWhiteSpace(newUsername, nameof(newUsername));
    public void SetPasswordHash(string passwordHash) => base.PasswordHash = Guards.NotNullOrWhiteSpace(passwordHash, nameof(passwordHash));

    public void SetSymbolAttachment(Guid? attachmentId)
    {
        SymbolAttachmentId = attachmentId == Guid.Empty ? null : attachmentId;
        Touch();
    }
}