public interface ISetupImportService
{
    Task ImportAsync(Guid userId, Stream fileStream, bool replaceExisting, CancellationToken ct);
}