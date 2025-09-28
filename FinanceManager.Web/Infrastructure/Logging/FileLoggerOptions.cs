namespace FinanceManager.Web.Infrastructure.Logging;

public sealed class FileLoggerOptions
{
    public string PathFormat { get; set; } = "logs/app-{date}.log"; // {date} → yyyyMMdd
    public int RetainedFileCountLimit { get; set; } = 30;
    public long FileSizeLimitBytes { get; set; } = 10 * 1024 * 1024; // 10 MB
    public bool RollOnFileSizeLimit { get; set; } = true;
    public bool Append { get; set; } = true;
    public bool IncludeScopes { get; set; } = false;
    public bool UseUtcTimestamp { get; set; } = false;
}