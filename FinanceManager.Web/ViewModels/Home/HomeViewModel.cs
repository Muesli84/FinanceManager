using FinanceManager.Shared.Dtos.Securities;
using FinanceManager.Shared.Dtos.Statements;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.Localization;

namespace FinanceManager.Web.ViewModels.Home;

/// <summary>
/// View model for the home/dashboard page.
/// </summary>
public sealed class HomeViewModel : ViewModelBase
{
    private readonly FinanceManager.Shared.IApiClient _api;
    private List<MassImportFileUploadDto> _pendingUploads = [];

    /// <summary>
    /// Initializes a new instance of <see cref="HomeViewModel"/>.
    /// </summary>
    public HomeViewModel(IServiceProvider sp) : base(sp)
    {
        _api = sp.GetRequiredService<FinanceManager.Shared.IApiClient>();
    }

    /// <summary>
    /// Gets a value indicating whether an import/upload operation is currently in progress.
    /// </summary>
    public bool UploadInProgress { get; private set; }

    /// <summary>
    /// Total number of files expected to be uploaded in the current batch.
    /// </summary>
    public int UploadTotal { get; private set; }

    /// <summary>
    /// Number of files already processed in the current upload batch.
    /// </summary>
    public int UploadDone { get; private set; }

    /// <summary>
    /// Name of the file currently being uploaded.
    /// </summary>
    public string? CurrentFileName { get; private set; }

    /// <summary>
    /// Indicates whether the last import produced at least one successful result.
    /// </summary>
    public bool ImportSuccess { get; private set; }

    /// <summary>
    /// First created statement draft id of the last successful import.
    /// </summary>
    public Guid? FirstDraftId { get; private set; }

    /// <summary>
    /// Optional split information returned by legacy statement upload.
    /// </summary>
    public ImportSplitInfoDto? SplitInfo { get; private set; }

    /// <summary>
    /// Currently selected mass import dialog policy.
    /// </summary>
    public MassImportDialogPolicy MassImportDialogPolicy { get; private set; } = MassImportDialogPolicy.OnMissingInformation;

    /// <summary>
    /// Pending confirmation model for mixed mass import.
    /// </summary>
    public MassImportBatchResultDto? PendingMassImport { get; private set; }

    /// <summary>
    /// Active securities for manual assignment in the mass import dialog.
    /// </summary>
    public IReadOnlyList<SecurityDto> ActiveSecurities { get; private set; } = [];

    /// <summary>
    /// KPI edit mode state.
    /// </summary>
    public bool KpiEditMode { get; private set; }

    /// <summary>
    /// Upload progress in percent.
    /// </summary>
    /// <returns>The result.</returns>
    public int UploadPercent => UploadTotal == 0 ? 0 : (int)Math.Round((double)(UploadDone * 100m / UploadTotal));

    /// <summary>
    /// Toggles KPI edit mode.
    /// </summary>
    public void ToggleKpiEditMode()
    {
        KpiEditMode = !KpiEditMode;
        RaiseStateChanged();
    }

    /// <summary>
    /// Starts upload state tracking for a new batch.
    /// </summary>
    /// <param name="total">The total.</param>
    public void StartUpload(int total)
    {
        UploadInProgress = true;
        UploadTotal = total;
        UploadDone = 0;
        CurrentFileName = null;
        ImportSuccess = false;
        FirstDraftId = null;
        SplitInfo = null;
        PendingMassImport = null;
        _pendingUploads = [];
        ActiveSecurities = [];
        RaiseStateChanged();
    }

    /// <summary>
    /// Confirms and executes a pending mass import batch.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    public async Task ConfirmMassImportAsync(CancellationToken ct = default)
    {
        if (PendingMassImport == null || _pendingUploads.Count == 0)
        {
            return;
        }

        if (!await ConfirmationService.ConfirmAsync(new(
            TitleResourceKey: "Confirmation_Finalize_Title",
            MessageResourceKey: "Confirmation_Finalize_Message",
            Severity: ConfirmationSeverity.Warning)))
        {
            return;
        }

        var request = new MassImportBatchRequestDto
        {
            DialogPolicy = MassImportDialogPolicy,
            ConfirmExecution = true,
            Files = _pendingUploads,
            Decisions = PendingMassImport.Files
                .Select(file => new MassImportFileDecisionDto
                {
                    FileId = file.FileId,
                    Excluded = file.Excluded,
                    SelectedSecurityId = file.SelectedSecurityId
                })
                .ToList()
        };

        var result = await _api.StatementDrafts_ProcessMassImportAsync(request, ct);
        if (result == null)
        {
            return;
        }

        PendingMassImport = null;
        _pendingUploads = [];
        ApplyMassImportResult(result);
        RaiseStateChanged();
    }

    /// <summary>
    /// Cancels the pending mass import dialog.
    /// </summary>
    public void CancelMassImportDialog()
    {
        PendingMassImport = null;
        _pendingUploads = [];
        RaiseStateChanged();
    }

    /// <summary>
    /// Updates exclusion state for one pending file.
    /// </summary>
    /// <param name="fileId">The file id.</param>
    /// <param name="excluded">The excluded.</param>
    public void SetPendingFileExcluded(Guid fileId, bool excluded)
    {
        if (PendingMassImport == null)
        {
            return;
        }

        var updated = PendingMassImport.Files.Select(file =>
        {
            if (file.FileId != fileId)
            {
                return file;
            }

            if (!IsPendingFileSelectable(file))
            {
                file.Excluded = true;
                return file;
            }

            file.Excluded = excluded;
            file.DecisionSource = MassImportDecisionSource.UserConfirmed;
            return file;
        }).ToList();

        PendingMassImport.Files = updated;
        RaiseStateChanged();
    }

    /// <summary>
    /// Updates selected security for one pending file.
    /// </summary>
    /// <param name="fileId">The file id.</param>
    /// <param name="securityId">The security id.</param>
    public void SetPendingFileSecurity(Guid fileId, Guid? securityId)
    {
        if (PendingMassImport == null)
        {
            return;
        }

        var updated = PendingMassImport.Files.Select(file =>
        {
            if (file.FileId != fileId)
            {
                return file;
            }

            if (!IsPendingFileSelectable(file))
            {
                file.Excluded = true;
                file.CanImport = false;
                return file;
            }

            file.SelectedSecurityId = securityId;
            file.CanImport = securityId.HasValue;
            file.ValidationMessage = securityId.HasValue ? null : "Missing security assignment.";
            file.DecisionSource = MassImportDecisionSource.UserConfirmed;
            return file;
        }).ToList();

        PendingMassImport.Files = updated;
        RaiseStateChanged();
    }

    /// <summary>
    /// Builds ribbon actions for home view.
    /// </summary>
    /// <param name="localizer">The localizer.</param>
    /// <returns>The result.</returns>
    protected override IReadOnlyList<UiRibbonRegister>? GetRibbonRegisterDefinition(IStringLocalizer localizer)
    {
        var importAction = new UiRibbonAction(
            Id: "Import",
            Label: localizer["Ribbon_Import"],
            IconSvg: "<svg><use href='/icons/sprite.svg#upload'/></svg>",
            Size: UiRibbonItemSize.Large,
            Disabled: false,
            Tooltip: null,
            Callback: null)
        {
            FileCallback = async (InputFileChangeEventArgs e) =>
            {
                var files = e.GetMultipleFiles();
                if (files == null || files.Count == 0)
                {
                    return;
                }

                await ProcessMassImportSelectionAsync(files);
            }
        };

        var kpiAction = new UiRibbonAction(
            Id: "ToggleKpi",
            Label: KpiEditMode ? localizer["Ribbon_Kpi_Done"] : localizer["Ribbon_Kpi_Edit"],
            IconSvg: KpiEditMode ? "<svg><use href='/icons/sprite.svg#check'/></svg>" : "<svg><use href='/icons/sprite.svg#edit'/></svg>",
            Size: UiRibbonItemSize.Large,
            Disabled: false,
            Tooltip: null,
            Callback: new Func<Task>(() => { ToggleKpiEditMode(); return Task.CompletedTask; }));

        var importTab = new UiRibbonTab(localizer["Ribbon_Group_Import"], new List<UiRibbonAction> { importAction });
        var kpiTab = new UiRibbonTab(localizer["Ribbon_Group_KPI"], new List<UiRibbonAction> { kpiAction });
        return new List<UiRibbonRegister> { new(UiRibbonRegisterKind.Actions, new List<UiRibbonTab> { importTab, kpiTab }) };
    }

    private async Task ProcessMassImportSelectionAsync(IReadOnlyList<IBrowserFile> files)
    {
        StartUpload(files.Count);
        var uploads = new List<MassImportFileUploadDto>(files.Count);

        try
        {
            var settings = await _api.UserSettings_GetImportSplitAsync();
            MassImportDialogPolicy = settings?.MassImportDialogPolicy ?? MassImportDialogPolicy.OnMissingInformation;
        }
        catch
        {
            MassImportDialogPolicy = MassImportDialogPolicy.OnMissingInformation;
        }

        try
        {
            foreach (var file in files)
            {
                CurrentFileName = file.Name;
                RaiseStateChanged();

                await using var stream = file.OpenReadStream(10_000_000);
                using var memory = new MemoryStream();
                await stream.CopyToAsync(memory);

                uploads.Add(new MassImportFileUploadDto
                {
                    FileId = Guid.NewGuid(),
                    FileName = file.Name,
                    ContentType = file.ContentType,
                    Content = memory.ToArray()
                });

                UploadDone++;
                RaiseStateChanged();
            }

            UploadInProgress = false;
            CurrentFileName = null;

            var request = new MassImportBatchRequestDto
            {
                DialogPolicy = MassImportDialogPolicy,
                ConfirmExecution = false,
                Files = uploads
            };

            var result = await _api.StatementDrafts_ProcessMassImportAsync(request);
            if (result == null)
            {
                return;
            }

            if (result.RequiresConfirmation)
            {
                PendingMassImport = NormalizePendingMassImportResult(result);
                _pendingUploads = uploads;
                ActiveSecurities = (await _api.Securities_ListAsync(onlyActive: true))
                    .OrderBy(security => security.Name)
                    .ThenBy(security => security.Identifier)
                    .ToList();
                RaiseStateChanged();
                return;
            }

            ApplyMassImportResult(result);
            RaiseStateChanged();
        }
        finally
        {
            UploadInProgress = false;
            CurrentFileName = null;
            UploadTotal = files.Count;
            UploadDone = files.Count;
            RaiseStateChanged();
        }
    }

    private void ApplyMassImportResult(MassImportBatchResultDto result)
    {
        var firstDraft = result.Files.FirstOrDefault(file => file.StatementDraftId.HasValue);
        FirstDraftId = firstDraft?.StatementDraftId;
        ImportSuccess = result.Files.Any(file => file.ExecutionStatus == MassImportFileExecutionStatus.Imported);
        SplitInfo = null;
    }

    private static MassImportBatchResultDto NormalizePendingMassImportResult(MassImportBatchResultDto result)
    {
        result.Files = result.Files.Select(file =>
        {
            if (!IsPendingFileSelectable(file))
            {
                file.Excluded = true;
                file.CanImport = false;
            }

            return file;
        }).ToList();

        return result;
    }

    private static bool IsPendingFileSelectable(MassImportBatchFileResultDto file)
        => file.FileType != MassImportFileType.Unknown && !string.IsNullOrWhiteSpace(file.ServiceKey);
}
