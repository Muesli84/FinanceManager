namespace FinanceManager.Web.Infrastructure.Attachments;

/// <summary>
/// Options that control attachment upload limitations and allowed mime types.
/// </summary>
public sealed class AttachmentUploadOptions
{
    private const long MaxBoundedLimitBytes = 9_223_372_036_854_775_806L;

    /// <summary>
    /// Default maximum allowed upload size in bytes. Default is 10 MB.
    /// </summary>
    public const long DefaultMaxSizeBytes = 10L * 1024L * 1024L;

    /// <summary>
    /// Maximum allowed upload size in bytes. Default is 10 MB.
    /// </summary>
    public long MaxSizeBytes { get; set; } = DefaultMaxSizeBytes;

    /// <summary>
    /// Gets the effective maximum upload size after applying fallback and upper bounds.
    /// </summary>
    /// <param name="MaxSizeBytes">The max size bytes.</param>
    /// <returns>The result.</returns>
    public long NormalizedMaxSizeBytes => NormalizeMaxSizeBytes(MaxSizeBytes);

    /// <summary>
    /// Normalizes configured upload size values for all attachment request limits.
    /// </summary>
    /// <param name="configuredMaxSizeBytes">The configured max size bytes.</param>
    /// <returns>The result.</returns>
    public static long NormalizeMaxSizeBytes(long configuredMaxSizeBytes)
    {
        if (configuredMaxSizeBytes <= 0)
        {
            return DefaultMaxSizeBytes;
        }

        return configuredMaxSizeBytes > MaxBoundedLimitBytes
            ? MaxBoundedLimitBytes
            : configuredMaxSizeBytes;
    }

    /// <summary>
    /// Whitelist of allowed MIME types for uploaded files. Requests with a content type not in this list should be rejected.
    /// </summary>
    /// <remarks>
    /// The default list contains common document and image formats as well as archive types.
    /// Customize in configuration when additional types are required.
    /// </remarks>
    public string[] AllowedMimeTypes { get; set; } = new[]
    {
        "application/pdf",
        "image/png",
        "image/jpeg",
        "image/svg+xml",
        // support Windows icon formats
        "image/x-icon",
        "image/vnd.microsoft.icon",
        "text/plain",
        "text/csv",
        "application/zip"
    };
}
