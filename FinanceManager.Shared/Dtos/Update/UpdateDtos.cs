namespace FinanceManager.Shared.Dtos.Update;

/// <summary>
/// Describes the current state of the application update process.
/// </summary>
public enum UpdateStatusKind
{
    /// <summary>No update is available.</summary>
    NoUpdate = 0,
    /// <summary>An update check is currently running.</summary>
    Checking = 1,
    /// <summary>An update is available for download.</summary>
    Available = 2,
    /// <summary>An update package is being downloaded.</summary>
    Downloading = 3,
    /// <summary>The update package is downloaded and ready to install.</summary>
    Ready = 4,
    /// <summary>The update is being installed.</summary>
    Installing = 5,
    /// <summary>The update process failed.</summary>
    Failed = 6
}

/// <summary>
/// Describes an available update release including its downloadable assets.
/// </summary>
/// <param name="Version">Version string of the release.</param>
/// <param name="ReleaseNotes">Optional release notes text.</param>
/// <param name="PublishedAt">Optional timestamp when the release was published.</param>
/// <param name="RepositoryOwner">Owner of the source repository.</param>
/// <param name="RepositoryName">Name of the source repository.</param>
/// <param name="Assets">Downloadable assets that belong to the release.</param>
/// <returns>The result.</returns>
public sealed record UpdateMetadataDto(
    string Version,
    string? ReleaseNotes,
    DateTimeOffset? PublishedAt,
    string RepositoryOwner,
    string RepositoryName,
    IReadOnlyList<UpdateAssetDto> Assets);

/// <summary>
/// Describes a single downloadable update asset for a specific platform.
/// </summary>
/// <param name="Platform">Target platform of the asset.</param>
/// <param name="RuntimeIdentifier">Runtime identifier the asset was built for.</param>
/// <param name="AssetName">File name of the asset.</param>
/// <param name="AssetUrl">Download URL of the asset.</param>
/// <param name="Sha256">SHA-256 checksum of the asset.</param>
/// <param name="SizeBytes">Size of the asset in bytes.</param>
/// <returns>The result.</returns>
public sealed record UpdateAssetDto(
    string Platform,
    string RuntimeIdentifier,
    string AssetName,
    string AssetUrl,
    string Sha256,
    long SizeBytes);

/// <summary>
/// Metadata about the currently installed release.
/// </summary>
/// <param name="Version">Installed version, if known.</param>
/// <param name="PublishedAt">Timestamp when the installed release was published, if known.</param>
/// <param name="CommitSha">Commit SHA the installed release was built from, if known.</param>
/// <param name="Repository">Repository the installed release originates from, if known.</param>
/// <param name="RuntimeIdentifier">Runtime identifier of the installed release, if known.</param>
/// <returns>The result.</returns>
public sealed record InstalledReleaseMetadataDto(
    string? Version,
    DateTimeOffset? PublishedAt,
    string? CommitSha,
    string? Repository,
    string? RuntimeIdentifier);

/// <summary>
/// Current status snapshot of the update subsystem.
/// </summary>
/// <param name="Status">Current update state.</param>
/// <param name="InstalledVersion">Version currently installed, if known.</param>
/// <param name="InstalledReleasePublishedAt">Publish timestamp of the installed release, if known.</param>
/// <param name="AvailableVersion">Version of the available update, if any.</param>
/// <param name="CurrentPlatform">Platform identifier of the running instance.</param>
/// <param name="LastCheckedAt">Timestamp of the last update check, if any.</param>
/// <param name="LastError">Last error message, if the update process failed.</param>
/// <param name="DownloadedAssetName">Name of the downloaded asset, if a download finished.</param>
/// <param name="IsLocked">Whether an update operation is currently locked.</param>
/// <param name="LockCreatedAt">Timestamp when the lock was created, if locked.</param>
/// <param name="ScheduledInstallTime">Scheduled time for automatic installation, if configured.</param>
/// <param name="AvailableUpdate">Metadata of the available update, if any.</param>
/// <returns>The result.</returns>
public sealed record UpdateStatusDto(
    UpdateStatusKind Status,
    string? InstalledVersion,
    DateTimeOffset? InstalledReleasePublishedAt,
    string? AvailableVersion,
    string CurrentPlatform,
    DateTimeOffset? LastCheckedAt,
    string? LastError,
    string? DownloadedAssetName,
    bool IsLocked,
    DateTimeOffset? LockCreatedAt,
    TimeOnly? ScheduledInstallTime,
    UpdateMetadataDto? AvailableUpdate);

/// <summary>
/// Effective update settings of the application.
/// </summary>
/// <param name="Enabled">Whether automatic update checks are enabled.</param>
/// <param name="RepositoryOwner">Owner of the source repository.</param>
/// <param name="RepositoryName">Name of the source repository.</param>
/// <param name="ManifestAssetName">Name of the manifest asset used for update checks.</param>
/// <param name="SourceCheckStartTime">Start of the daily time window for update checks.</param>
/// <param name="SourceCheckEndTime">End of the daily time window for update checks.</param>
/// <param name="ScheduledInstallTime">Scheduled time for automatic installation, if configured.</param>
/// <param name="ServiceName">Optional service name used for the update installation.</param>
/// <param name="ExecutablePath">Optional executable path used for the update installation.</param>
/// <param name="WorkingDirectory">Working directory used during update installation.</param>
/// <param name="HealthTimeoutSeconds">Timeout in seconds for the post-update health check.</param>
/// <param name="IncludePrereleases">Whether prereleases are considered during update checks.</param>
/// <returns>The result.</returns>
public sealed record UpdateSettingsDto(
    bool Enabled,
    string RepositoryOwner,
    string RepositoryName,
    string ManifestAssetName,
    TimeOnly SourceCheckStartTime,
    TimeOnly SourceCheckEndTime,
    TimeOnly? ScheduledInstallTime,
    string? ServiceName,
    string? ExecutablePath,
    string WorkingDirectory,
    int HealthTimeoutSeconds,
    bool IncludePrereleases);

/// <summary>
/// Request payload used to update the update settings.
/// </summary>
/// <param name="Enabled">Whether automatic update checks are enabled.</param>
/// <param name="RepositoryOwner">Owner of the source repository, or <c>null</c> to keep the current value.</param>
/// <param name="RepositoryName">Name of the source repository, or <c>null</c> to keep the current value.</param>
/// <param name="ManifestAssetName">Name of the manifest asset, or <c>null</c> to keep the current value.</param>
/// <param name="SourceCheckStartTime">Start of the daily time window for update checks.</param>
/// <param name="SourceCheckEndTime">End of the daily time window for update checks.</param>
/// <param name="ScheduledInstallTime">Scheduled time for automatic installation, if configured.</param>
/// <param name="ServiceName">Optional service name used for the update installation.</param>
/// <param name="ExecutablePath">Optional executable path used for the update installation.</param>
/// <param name="WorkingDirectory">Working directory used during update installation, or <c>null</c> to keep the current value.</param>
/// <param name="HealthTimeoutSeconds">Timeout in seconds for the post-update health check.</param>
/// <param name="IncludePrereleases">Whether prereleases are considered during update checks.</param>
/// <returns>The result.</returns>
public sealed record UpdateSettingsUpdateRequest(
    bool Enabled,
    string? RepositoryOwner,
    string? RepositoryName,
    string? ManifestAssetName,
    TimeOnly SourceCheckStartTime,
    TimeOnly SourceCheckEndTime,
    TimeOnly? ScheduledInstallTime,
    string? ServiceName,
    string? ExecutablePath,
    string? WorkingDirectory,
    int HealthTimeoutSeconds,
    bool IncludePrereleases);

/// <summary>
/// Request payload used to schedule the automatic update installation time.
/// </summary>
/// <param name="ScheduledInstallTime">Scheduled installation time, or <c>null</c> to disable scheduling.</param>
/// <returns>The result.</returns>
public sealed record UpdateScheduleRequest(TimeOnly? ScheduledInstallTime);

/// <summary>
/// Request payload used to start an update installation.
/// </summary>
/// <param name="ConfirmDowntime">Whether the user confirmed the required downtime.</param>
/// <returns>The result.</returns>
public sealed record UpdateStartRequest(bool ConfirmDowntime);

/// <summary>
/// Request payload used to reset a stale update lock.
/// </summary>
/// <param name="Reason">Optional reason for resetting the lock.</param>
/// <returns>The result.</returns>
public sealed record UpdateLockResetRequest(string? Reason);

/// <summary>
/// Result of an update check operation.
/// </summary>
/// <param name="UpdateAvailable">Whether an update is available.</param>
/// <param name="Status">Current update status snapshot.</param>
/// <param name="Message">Optional informational or error message.</param>
/// <returns>The result.</returns>
public sealed record UpdateCheckResultDto(
    bool UpdateAvailable,
    UpdateStatusDto Status,
    string? Message);
