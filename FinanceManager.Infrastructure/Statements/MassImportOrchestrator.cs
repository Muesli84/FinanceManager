using System.Text;
using FinanceManager.Application.Securities;
using FinanceManager.Application.Statements;
using FinanceManager.Infrastructure.Statements.Files;
using FinanceManager.Infrastructure.Statements.Parsers;
using FinanceManager.Shared.Dtos.Securities;
using FinanceManager.Shared.Dtos.Statements;
using Microsoft.Extensions.Logging;
using StatementParser = FinanceManager.Infrastructure.Statements.Parsers.IStatementFileParser;

namespace FinanceManager.Infrastructure.Statements;

/// <summary>
/// Orchestrates mixed start-page imports (account statements and security prices).
/// </summary>
public sealed class MassImportOrchestrator : IMassImportOrchestrator
{
    private readonly IStatementDraftService _statementDraftService;
    private readonly IStatementFileFactory _statementFileFactory;
    private readonly IEnumerable<StatementParser> _statementParsers;
    private readonly ISecurityService _securityService;
    private readonly ISecurityPriceImportServiceFactory _securityPriceImportServiceFactory;
    private readonly ILogger<MassImportOrchestrator> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="MassImportOrchestrator"/> class.
    /// </summary>
    /// <param name="statementDraftService">The statement draft service.</param>
    /// <param name="statementFileFactory">The statement file factory.</param>
    /// <param name="statementParsers">The statement parsers.</param>
    /// <param name="securityService">The security service.</param>
    /// <param name="securityPriceImportServiceFactory">The security price import service factory.</param>
    /// <param name="logger">Logger instance.</param>
    public MassImportOrchestrator(
        IStatementDraftService statementDraftService,
        IStatementFileFactory statementFileFactory,
        IEnumerable<StatementParser> statementParsers,
        ISecurityService securityService,
        ISecurityPriceImportServiceFactory securityPriceImportServiceFactory,
        ILogger<MassImportOrchestrator> logger)
    {
        _statementDraftService = statementDraftService;
        _statementFileFactory = statementFileFactory;
        _statementParsers = statementParsers;
        _securityService = securityService;
        _securityPriceImportServiceFactory = securityPriceImportServiceFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<MassImportBatchResultDto> ProcessAsync(Guid ownerUserId, MassImportBatchRequestDto request, string traceId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var uploads = request.Files ?? [];

        var batchId = Guid.NewGuid();
        var activeSecurities = await _securityService.ListAsync(ownerUserId, onlyActive: true, ct);
        var decisions = request.Decisions.ToDictionary(x => x.FileId, x => x);
        var fileResults = new List<MassImportBatchFileResultDto>(uploads.Count);

        foreach (var upload in uploads)
        {
            ct.ThrowIfCancellationRequested();

            var fileId = upload.FileId == Guid.Empty ? Guid.NewGuid() : upload.FileId;
            var analysis = AnalyzeFile(upload, activeSecurities);
            var decisionSource = MassImportDecisionSource.AutoDetected;
            var excluded = false;
            var selectedSecurityId = analysis.GuessedSecurityId;
            var canImport = analysis.FileType != MassImportFileType.Unknown && !string.IsNullOrWhiteSpace(analysis.ServiceKey);
            var validationMessage = analysis.ValidationMessage;
            var securityAutoGuessed = analysis.GuessedSecurityId.HasValue;

            if (decisions.TryGetValue(fileId, out var decision))
            {
                excluded = decision.Excluded;
                if (analysis.FileType == MassImportFileType.SecurityPrices)
                {
                    selectedSecurityId = decision.SelectedSecurityId;
                    securityAutoGuessed = decision.SelectedSecurityId.HasValue && decision.SelectedSecurityId == analysis.GuessedSecurityId;
                }

                decisionSource = MassImportDecisionSource.UserConfirmed;
            }

            if (analysis.FileType == MassImportFileType.SecurityPrices)
            {
                canImport = canImport && selectedSecurityId.HasValue;
                if (!selectedSecurityId.HasValue)
                {
                    validationMessage = "Missing security assignment.";
                }
            }

            var fileResult = new MassImportBatchFileResultDto
            {
                FileId = fileId,
                FileName = upload.FileName,
                FileType = analysis.FileType,
                ServiceKey = analysis.ServiceKey,
                ServiceDisplayName = analysis.ServiceDisplayName,
                CanImport = canImport,
                Excluded = excluded,
                SelectedSecurityId = selectedSecurityId,
                SecurityAutoGuessed = securityAutoGuessed,
                DecisionSource = decisionSource,
                ExecutionStatus = MassImportFileExecutionStatus.Pending,
                ValidationMessage = validationMessage
            };

            LogAudit(batchId, traceId, fileResult);
            fileResults.Add(fileResult);
        }

        var dialogRequired = IsDialogRequired(request.DialogPolicy, fileResults);
        var requiresConfirmation = dialogRequired && !request.ConfirmExecution;
        if (requiresConfirmation)
        {
            return new MassImportBatchResultDto
            {
                BatchId = batchId,
                DialogRequired = true,
                DialogSkipped = false,
                RequiresConfirmation = true,
                Files = fileResults
            };
        }

        for (var index = 0; index < uploads.Count; index++)
        {
            ct.ThrowIfCancellationRequested();

            var upload = uploads[index];
            var result = fileResults[index];
            if (result.Excluded || !result.CanImport)
            {
                result.ExecutionStatus = MassImportFileExecutionStatus.Skipped;
                LogAudit(batchId, traceId, result);
                continue;
            }

            try
            {
                switch (result.FileType)
                {
                    case MassImportFileType.AccountStatement:
                        await ImportStatementAsync(ownerUserId, upload, result, ct);
                        break;
                    case MassImportFileType.SecurityPrices:
                        await ImportSecurityPricesAsync(ownerUserId, upload, result, ct);
                        break;
                    default:
                        result.ExecutionStatus = MassImportFileExecutionStatus.Skipped;
                        break;
                }
            }
            catch (Exception ex)
            {
                result.ExecutionStatus = MassImportFileExecutionStatus.Failed;
                result.ValidationMessage = ex.Message;
                _logger.LogError(ex, "Mass import execution failed for file {FileName}", result.FileName);
            }

            LogAudit(batchId, traceId, result);
        }

        return new MassImportBatchResultDto
        {
            BatchId = batchId,
            DialogRequired = dialogRequired,
            DialogSkipped = !dialogRequired,
            RequiresConfirmation = false,
            Files = fileResults
        };
    }

    private async Task ImportStatementAsync(Guid ownerUserId, MassImportFileUploadDto upload, MassImportBatchFileResultDto result, CancellationToken ct)
    {
        var draftIds = new List<Guid>();
        await foreach (var draft in _statementDraftService.CreateDraftAsync(ownerUserId, upload.FileName, upload.Content, ct))
        {
            draftIds.Add(draft.DraftId);
        }

        if (draftIds.Count == 0)
        {
            result.ExecutionStatus = MassImportFileExecutionStatus.Failed;
            result.ValidationMessage = "No statement draft created.";
            return;
        }

        result.StatementDraftId = draftIds[0];
        result.StatementDraftIds = draftIds;
        result.ExecutionStatus = MassImportFileExecutionStatus.Imported;
    }

    private async Task ImportSecurityPricesAsync(Guid ownerUserId, MassImportFileUploadDto upload, MassImportBatchFileResultDto result, CancellationToken ct)
    {
        if (!result.SelectedSecurityId.HasValue)
        {
            result.ExecutionStatus = MassImportFileExecutionStatus.Failed;
            result.ValidationMessage = "Missing security assignment.";
            return;
        }

        var security = await _securityService.GetAsync(result.SelectedSecurityId.Value, ownerUserId, ct);
        if (security is null || !security.IsActive)
        {
            result.ExecutionStatus = MassImportFileExecutionStatus.Failed;
            result.ValidationMessage = "Assigned security is not available or inactive.";
            return;
        }

        var context = new SecurityPriceImportContext(result.ServiceKey, upload.FileName, upload.ContentType);
        var importService = _securityPriceImportServiceFactory.Resolve(context);
        await using var stream = new MemoryStream(upload.Content, writable: false);
        var importResult = await importService.ImportAsync(ownerUserId, security.Id, stream, context, ct);
        if (importResult.Inserted + importResult.Updated + importResult.Unchanged == 0)
        {
            result.ExecutionStatus = MassImportFileExecutionStatus.Failed;
            result.ValidationMessage = "No valid security price rows found.";
            result.PriceImportResult = importResult;
            return;
        }

        result.ExecutionStatus = MassImportFileExecutionStatus.Imported;
        result.PriceImportResult = importResult;
    }

    private (MassImportFileType FileType, string ServiceKey, string ServiceDisplayName, Guid? GuessedSecurityId, string? ValidationMessage) AnalyzeFile(MassImportFileUploadDto upload, IReadOnlyList<SecurityDto> activeSecurities)
    {
        if (upload.Content.Length == 0)
        {
            return (MassImportFileType.Unknown, string.Empty, string.Empty, null, "File is empty.");
        }

        var statementFile = _statementFileFactory.Load(upload.FileName, upload.Content);
        if (statementFile != null)
        {
            var parsed = _statementParsers
                .Select(parser => parser.Parse(statementFile))
                .Where(results => results is not null)
                .SelectMany(results => results!)
                .FirstOrDefault(result => result.Movements.Any());

            if (parsed != null)
            {
                var service = ResolveStatementService(statementFile.GetType().Name);
                return (MassImportFileType.AccountStatement, service.ServiceKey, service.DisplayName, null, null);
            }
        }

        var context = new SecurityPriceImportContext(Provider: null, upload.FileName, upload.ContentType);
        if (!_securityPriceImportServiceFactory.TryResolveByContent(context, upload.Content, out _, out var inspection) || inspection is null)
        {
            return (MassImportFileType.Unknown, string.Empty, string.Empty, null, "File type could not be recognized.");
        }

        var guessedSecurityId = GuessSecurity(inspection.DetectedSecurityName ?? string.Empty, upload.FileName, activeSecurities);
        var validationMessage = guessedSecurityId.HasValue ? null : "Missing security assignment.";
        return (MassImportFileType.SecurityPrices, inspection.ServiceKey, inspection.ServiceDisplayName, guessedSecurityId, validationMessage);
    }

    private static (string ServiceKey, string DisplayName) ResolveStatementService(string statementFileTypeName)
    {
        if (statementFileTypeName.Contains("Wuestenrot", StringComparison.OrdinalIgnoreCase))
        {
            return ("wuestenrot", "Wüstenrot");
        }

        if (statementFileTypeName.Contains("Sparkasse", StringComparison.OrdinalIgnoreCase))
        {
            return ("sparkasse", "Sparkasse");
        }

        if (statementFileTypeName.Contains("Barclays", StringComparison.OrdinalIgnoreCase))
        {
            return ("barclays", "Barclays");
        }

        if (statementFileTypeName.Contains("ING", StringComparison.OrdinalIgnoreCase))
        {
            return ("ing", "ING");
        }

        if (statementFileTypeName.Contains("Backup", StringComparison.OrdinalIgnoreCase))
        {
            return ("backup", "Backup");
        }

        return ("statement", "Statement");
    }

    private static Guid? GuessSecurity(string detectedSecurityName, string fileName, IReadOnlyList<SecurityDto> activeSecurities)
    {
        var normalizedDetectedSecurityName = NormalizeName(detectedSecurityName);
        var normalizedFileName = NormalizeName(Path.GetFileNameWithoutExtension(fileName));
        var matches = activeSecurities
            .Where(security =>
                ContainsToken(normalizedDetectedSecurityName, NormalizeName(security.Name)) ||
                ContainsToken(normalizedDetectedSecurityName, NormalizeName(security.Identifier)) ||
                ContainsToken(normalizedDetectedSecurityName, NormalizeName(security.AlphaVantageCode)) ||
                ContainsToken(normalizedFileName, NormalizeName(security.Name)) ||
                ContainsToken(normalizedFileName, NormalizeName(security.Identifier)) ||
                ContainsToken(normalizedFileName, NormalizeName(security.AlphaVantageCode)))
            .Select(security => security.Id)
            .Distinct()
            .ToList();

        return matches.Count == 1 ? matches[0] : null;
    }

    private static bool ContainsToken(string haystack, string token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length < 3)
        {
            return false;
        }

        return haystack.Contains(token, StringComparison.Ordinal);
    }

    private static string NormalizeName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        foreach (var ch in value.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(ch);
            }
        }

        return builder.ToString();
    }

    private static bool IsDialogRequired(MassImportDialogPolicy policy, IReadOnlyList<MassImportBatchFileResultDto> files)
    {
        if (policy == MassImportDialogPolicy.AlwaysConfirm)
        {
            return true;
        }

        return files.Any(file =>
            file.FileType == MassImportFileType.Unknown ||
            !file.CanImport ||
            (file.FileType == MassImportFileType.SecurityPrices && !file.SelectedSecurityId.HasValue));
    }

    private void LogAudit(Guid batchId, string traceId, MassImportBatchFileResultDto file)
    {
        _logger.LogInformation(
            "MassImportAudit batchId={BatchId} fileId={FileId} fileName={FileName} fileType={FileType} serviceDisplayName={ServiceDisplayName} excluded={Excluded} selectedSecurityId={SelectedSecurityId} decisionSource={DecisionSource} executionStatus={ExecutionStatus} traceId={TraceId}",
            batchId,
            file.FileId,
            file.FileName,
            file.FileType,
            file.ServiceDisplayName,
            file.Excluded,
            file.SelectedSecurityId,
            file.DecisionSource,
            file.ExecutionStatus,
            traceId);
    }
}
