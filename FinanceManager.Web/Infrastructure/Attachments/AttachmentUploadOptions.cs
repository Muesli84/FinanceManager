namespace FinanceManager.Web.Infrastructure.Attachments;

public sealed class AttachmentUploadOptions
{
    // Default 10 MB
    public long MaxSizeBytes { get; set; } = 10L * 1024L * 1024L;

    // Default whitelist
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
