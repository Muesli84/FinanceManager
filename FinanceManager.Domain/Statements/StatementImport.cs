namespace FinanceManager.Domain.Statements;

public sealed class StatementImport : Entity, IAggregateRoot
{
    private StatementImport() { }
    public StatementImport(Guid accountId, ImportFormat format, string originalFileName)
    {
        AccountId = Guards.NotEmpty(accountId, nameof(accountId));
        Format = format;
        OriginalFileName = Guards.NotNullOrWhiteSpace(originalFileName, nameof(originalFileName));
        ImportedAtUtc = DateTime.UtcNow;
    }
    public Guid AccountId { get; private set; }
    public ImportFormat Format { get; private set; }
    public DateTime ImportedAtUtc { get; private set; } = DateTime.UtcNow;
    public string OriginalFileName { get; private set; } = null!;
    public int TotalEntries { get; private set; }
}