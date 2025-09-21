namespace FinanceManager.Domain.Users;

using FinanceManager.Shared.Dtos; // ImportSplitMode

public sealed class User : Entity, IAggregateRoot
{
    private User() { }
    public User(string username, string passwordHash, bool isAdmin)
    {
        Username = Guards.NotNullOrWhiteSpace(username, nameof(username));
        PasswordHash = Guards.NotNullOrWhiteSpace(passwordHash, nameof(passwordHash));
        IsAdmin = isAdmin;
        // Defaults for import split settings (FA-AUSZ-016)
        ImportSplitMode = ImportSplitMode.MonthlyOrFixed;
        ImportMaxEntriesPerDraft = 250;
        ImportMonthlySplitThreshold = 250; // equals Max by default
        ImportMinEntriesPerDraft = 8; // new default (FA-AUSZ-016-12)
    }

    public string Username { get; private set; } = null!;
    public string PasswordHash { get; private set; } = null!;
    public bool IsAdmin { get; private set; }
    public DateTime? LockedUntilUtc { get; private set; }
    public string? PreferredLanguage { get; private set; }
    public DateTime LastLoginUtc { get; private set; }
    public bool Active { get; private set; } = true;
    public int FailedLoginAttempts { get; private set; }

    // --- Import Split Settings (User Preferences) ---
    public ImportSplitMode ImportSplitMode { get; private set; } = ImportSplitMode.MonthlyOrFixed;
    public int ImportMaxEntriesPerDraft { get; private set; } = 250;
    public int? ImportMonthlySplitThreshold { get; private set; } = 250; // nullable to allow future unset -> fallback
    public int ImportMinEntriesPerDraft { get; private set; } = 1; // new minimum entries preference

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
        ResetFailedLoginAttempts();
    }
    public void SetLockedUntil(DateTime? utc)
    {
        LockedUntilUtc = utc;
        if (utc == null)
        {
            ResetFailedLoginAttempts();
        }
    }
    public void SetPreferredLanguage(string? lang) => PreferredLanguage = string.IsNullOrWhiteSpace(lang) ? null : lang.Trim();
    public void Deactivate() => Active = false;
    public void Activate() => Active = true;
    public void Rename(string newUsername) => Username = Guards.NotNullOrWhiteSpace(newUsername, nameof(newUsername));
    public void SetAdmin(bool isAdmin) => IsAdmin = isAdmin;
    public void SetPasswordHash(string passwordHash) => PasswordHash = Guards.NotNullOrWhiteSpace(passwordHash, nameof(passwordHash));
    public int IncrementFailedLoginAttempts() => ++FailedLoginAttempts;
    public void ResetFailedLoginAttempts() => FailedLoginAttempts = 0;
}