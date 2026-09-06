using FinanceManager.Shared.Dtos.Update;
using msTools.Updater;

namespace FinanceManager.Web.Services.Updates;

/// <summary>
/// Transfers the runtime-relevant fields of <see cref="UpdateSettingsDto"/> into the auto-update library's
/// singleton <see cref="AutoUpdateOptions"/>, so that changes made through the setup UI take effect immediately.
/// </summary>
public static class AutoUpdateOptionsMapper
{
    /// <summary>
    /// Daily source checks run once every 24 hours; the configured time range decides when the check is allowed.
    /// </summary>
    public const int DailySourceCheckIntervalMinutes = 24 * 60;

    /// <summary>
    /// Default start time for automatic update checks.
    /// </summary>
    /// <returns>The result.</returns>
    public static TimeOnly DefaultSourceCheckStartTime { get; } = new(20, 0);

    /// <summary>
    /// Default end time for automatic update checks.
    /// </summary>
    /// <returns>The result.</returns>
    public static TimeOnly DefaultSourceCheckEndTime { get; } = new(6, 0);

    /// <summary>
    /// Applies the given settings onto <paramref name="options"/>. If the configured source is a
    /// <see cref="AutoUpdateGithubSource"/>, it is replaced with a new instance reflecting the (possibly changed)
    /// repository owner, repository name and manifest asset name, so that changes made through the setup UI take
    /// effect on the next check instead of only after a restart. The previous source is disposed.
    /// </summary>
    /// <param name="options">The auto-update library's runtime-mutable options to update.</param>
    /// <param name="settings">The settings to apply.</param>
    public static void ApplySettings(AutoUpdateOptions options, UpdateSettingsDto settings)
    {
        options.Enabled = settings.Enabled;
        options.SourceCheck.Interval = DailySourceCheckIntervalMinutes;
        options.SourceCheck.TimeRanges = BuildSourceCheckTimeRanges(settings.SourceCheckStartTime, settings.SourceCheckEndTime).ToList();
        options.ServiceName = settings.ServiceName;
        options.ExecutablePath = settings.ExecutablePath;
        options.DownloadPath = settings.WorkingDirectory;
        options.HealthTimeoutSeconds = settings.HealthTimeoutSeconds;
        options.ScheduledInstallTime = settings.ScheduledInstallTime;
        options.AllowPrereleaseUpdates = settings.IncludePrereleases;

        if (options.Source is AutoUpdateGithubSource previousSource)
        {
            options.Source = AutoUpdateGithubSource.Create(
                settings.RepositoryOwner,
                settings.RepositoryName,
                settings.ManifestAssetName,
                settings.IncludePrereleases);
            previousSource.Dispose();
        }
    }

    /// <summary>
    /// Builds an <see cref="UpdateSettingsDto"/> from the runtime-relevant fields of <paramref name="options"/>.
    /// The repository owner, repository name and manifest asset name are not part of <see cref="AutoUpdateOptions"/>
    /// (they are FinanceManager-specific and live on <c>UpdateOptions</c>/the persisted settings), so they are
    /// supplied by the caller. The returned values are unnormalized; callers apply their own defaulting/clamping.
    /// </summary>
    /// <param name="options">The auto-update library's runtime-mutable options to read from.</param>
    /// <param name="repositoryOwner">The repository owner to include in the DTO.</param>
    /// <param name="repositoryName">The repository name to include in the DTO.</param>
    /// <param name="manifestAssetName">The manifest asset name to include in the DTO.</param>
    /// <returns>An <see cref="UpdateSettingsDto"/> reflecting the current state of <paramref name="options"/>.</returns>
    public static UpdateSettingsDto ToSettingsDto(AutoUpdateOptions options, string repositoryOwner, string repositoryName, string manifestAssetName)
    {
        var (sourceCheckStartTime, sourceCheckEndTime) = ReadSourceCheckWindow(options.SourceCheck.TimeRanges);

        return new(
            options.Enabled,
            repositoryOwner,
            repositoryName,
            manifestAssetName,
            sourceCheckStartTime,
            sourceCheckEndTime,
            options.ScheduledInstallTime,
            options.ServiceName,
            options.ExecutablePath,
            options.DownloadPath,
            options.HealthTimeoutSeconds,
            options.AllowPrereleaseUpdates);
    }

    /// <summary>
    /// Builds one source-check window per day. Windows crossing midnight are split into two same-day ranges because
    /// the updater evaluator receives only the current local day and time for each check.
    /// </summary>
    /// <param name="startTime">Inclusive start time of the daily check window.</param>
    /// <param name="endTime">Exclusive end time of the daily check window.</param>
    /// <returns>The updater-library time ranges covering the configured daily window.</returns>
    public static IReadOnlyList<SourceCheckTimeRange> BuildSourceCheckTimeRanges(TimeOnly startTime, TimeOnly endTime)
    {
        var ranges = new List<SourceCheckTimeRange>();
        foreach (var day in Enum.GetValues<DayOfWeek>())
        {
            AddDailyRanges(ranges, day, startTime, endTime);
        }

        return ranges;
    }

    private static void AddDailyRanges(List<SourceCheckTimeRange> ranges, DayOfWeek day, TimeOnly startTime, TimeOnly endTime)
    {
        if (startTime == endTime)
        {
            ranges.Add(new SourceCheckTimeRange { DayOfWeek = day, StartTime = TimeOnly.MinValue, EndTime = TimeOnly.MaxValue });
            return;
        }

        if (startTime < endTime)
        {
            ranges.Add(new SourceCheckTimeRange { DayOfWeek = day, StartTime = startTime, EndTime = endTime });
            return;
        }

        ranges.Add(new SourceCheckTimeRange { DayOfWeek = day, StartTime = startTime, EndTime = TimeOnly.MaxValue });
        ranges.Add(new SourceCheckTimeRange { DayOfWeek = day, StartTime = TimeOnly.MinValue, EndTime = endTime });
    }

    private static (TimeOnly StartTime, TimeOnly EndTime) ReadSourceCheckWindow(IReadOnlyList<SourceCheckTimeRange>? timeRanges)
    {
        if (timeRanges is null || timeRanges.Count == 0)
        {
            return (DefaultSourceCheckStartTime, DefaultSourceCheckEndTime);
        }

        var midnightEndTimes = timeRanges
            .Where(r => r.StartTime == TimeOnly.MinValue)
            .Select(r => r.EndTime)
            .Distinct()
            .ToList();
        var eveningStartTimes = timeRanges
            .Where(r => r.StartTime != TimeOnly.MinValue && r.EndTime == TimeOnly.MaxValue)
            .Select(r => r.StartTime)
            .Distinct()
            .ToList();
        if (eveningStartTimes.Count == 1 && midnightEndTimes.Count == 1)
        {
            return (eveningStartTimes[0], midnightEndTimes[0]);
        }

        var regularRanges = timeRanges
            .Where(r => r.StartTime < r.EndTime)
            .ToList();
        var regularStartTimes = regularRanges.Select(r => r.StartTime).Distinct().ToList();
        var regularEndTimes = regularRanges.Select(r => r.EndTime).Distinct().ToList();
        if (regularStartTimes.Count == 1 && regularEndTimes.Count == 1)
        {
            return (regularStartTimes[0], regularEndTimes[0]);
        }

        return (DefaultSourceCheckStartTime, DefaultSourceCheckEndTime);
    }
}
