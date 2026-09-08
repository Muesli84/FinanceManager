using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using FinanceManager.Shared.Dtos.Backups;

namespace FinanceManager.Web.ViewModels.Setup;

/// <summary>
/// View model responsible for managing backups in the setup area.
/// Provides operations to list, create, upload, delete and trigger restore of backups.
/// </summary>
public sealed class SetupBackupsViewModel : BaseViewModel, IUploadTrigger
{
    /// <summary>
    /// Initializes a new instance of <see cref="SetupBackupsViewModel"/>.
    /// </summary>
    /// <param name="sp">Service provider used to resolve dependencies (API client, localization, etc.).</param>
    public SetupBackupsViewModel(IServiceProvider sp) : base(sp)
    {
        _logger = sp.GetService<ILogger<SetupBackupsViewModel>>();
    }

    private readonly ILogger<SetupBackupsViewModel>? _logger;

    /// <summary>
    /// Represents a single backup item returned by the API and displayed in the UI.
    /// </summary>
    public sealed class BackupItem
    {
        /// <summary>
        /// Identifier of the backup.
        /// </summary>
        public Guid Id { get; set; }

        /// <summary>
        /// UTC timestamp when the backup was created.
        /// </summary>
        public DateTime CreatedUtc { get; set; }

        /// <summary>
        /// File name of the backup archive.
        /// </summary>
        public string FileName { get; set; } = string.Empty;

        /// <summary>
        /// Size of the backup in bytes.
        /// </summary>
        public long SizeBytes { get; set; }

        /// <summary>
        /// Source description of the backup (e.g. local, cloud provider).
        /// </summary>
        public string Source { get; set; } = string.Empty;
    }

    /// <summary>
    /// Currently loaded list of backups; <c>null</c> when not loaded yet.
    /// </summary>
    public List<BackupItem>? Backups { get; private set; }

    /// <summary>
    /// Indicates whether an operation that may take a while (create/upload/delete) is in progress.
    /// </summary>
    public bool Busy { get; private set; }

    /// <summary>
    /// Indicates whether a restore operation is currently active in the system.
    /// </summary>
    public bool HasActiveRestore { get; private set; }

    /// <summary>
    /// Event used to request the UI to open a file picker for upload. Subscribers should
    /// open the file dialog and call <see cref="UploadAsync"/> with the chosen stream.
    /// </summary>
    public event EventHandler? UploadRequested;

    /// <summary>
    /// Loads the list of backups from the API and populates the <see cref="Backups"/> collection.
    /// Any errors are captured via <see cref="FinanceManager.Web.ViewModels.Common.BaseViewModel.SetError(string?, string?)"/>.
    /// </summary>
    /// <param name="ct">Cancellation token used to cancel the operation.</param>
    /// <returns>A task that completes when the operation finishes.</returns>
    public async Task LoadBackupsAsync(CancellationToken ct = default)
    {
        try
        {
            SetError(null, null);
            var list = await ApiClient.Backups_ListAsync(ct);
            Backups = list?.Select(MapToBackupItem).ToList() ?? new List<BackupItem>();
        }
        catch (Exception ex)
        {
            HandleApiException(ex);
            Backups = new List<BackupItem>();
        }
        finally { RaiseStateChanged(); }
    }

    /// <summary>
    /// Creates a new backup via the API and inserts it at the top of the <see cref="Backups"/> list when successful.
    /// </summary>
    /// <param name="ct">Cancellation token used to cancel the operation.</param>
    /// <returns>A task that completes when the create operation has finished.</returns>
    public async Task CreateAsync(CancellationToken ct = default)
    {
        BeginBusyOperation();
        try
        {
            var created = await ApiClient.Backups_CreateAsync(ct);
            if (created is not null)
            {
                AddBackup(MapToBackupItem(created));
            }
        }
        catch (Exception ex)
        {
            HandleApiException(ex);
        }
        finally { Busy = false; RaiseStateChanged(); }
    }

    /// <summary>
    /// Starts applying the specified backup. The server may return the current restore status which is
    /// reflected via <see cref="HasActiveRestore"/>.
    /// </summary>
    /// <param name="id">Backup identifier to apply.</param>
    /// <param name="confirmationText">Confirmation text the user typed, expected to match the backup's file name.</param>
    /// <param name="expectedFileName">File name expected by the caller, used as an additional guard against applying the wrong backup.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes when the request finishes.</returns>
    public async Task StartApplyAsync(Guid id, string confirmationText, string expectedFileName, CancellationToken ct = default)
    {
        if (id == Guid.Empty) { return; }
        BeginBusyOperation();
        try
        {
            var request = new BackupRestoreRequestDto(confirmationText, expectedFileName);
            var status = await ApiClient.Backups_StartApplyAsync(id, request, ct);
            if (status is not null)
            {
                HasActiveRestore = status.Running;
            }
        }
        catch (Exception ex)
        {
            HandleApiException(ex);
        }
        finally { Busy = false; RaiseStateChanged(); }
    }

    /// <summary>
    /// Deletes the backup with the specified id via the API and removes it from the local list on success.
    /// </summary>
    /// <param name="id">Identifier of the backup to delete.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes when deletion has finished.</returns>
    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        if (!await ConfirmationService.ConfirmAsync(new(
            TitleResourceKey: "Confirmation_Delete_Title",
            MessageResourceKey: "Confirmation_Delete_Message",
            Severity: ConfirmationSeverity.Critical)))
        {
            return;
        }

        BeginBusyOperation();
        try
        {
            var ok = await ApiClient.Backups_DeleteAsync(id, ct);
            if (ok)
            {
                if (Backups is not null)
                {
                    Backups.RemoveAll(x => x.Id == id);
                }
            }
        }
        catch (Exception ex)
        {
            HandleApiException(ex);
        }
        finally { Busy = false; RaiseStateChanged(); }
    }

    /// <summary>
    /// Uploads a backup file stream to the server and adds the returned backup to the local list.
    /// </summary>
    /// <param name="stream">Stream containing the backup file data. Must not be <c>null</c>.</param>
    /// <param name="fileName">Name of the uploaded file.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes when the upload has finished.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="stream"/> is <c>null</c>.</exception>
    public async Task UploadAsync(Stream stream, string fileName, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        BeginBusyOperation();
        try
        {
            var created = await ApiClient.Backups_UploadAsync(stream, fileName, ct);
            if (created is not null)
            {
                AddBackup(MapToBackupItem(created));
            }
        }
        catch (Exception ex)
        {
            HandleApiException(ex);
        }
        finally { Busy = false; RaiseStateChanged(); }
    }

    private void AddBackup(BackupItem item)
    {
        Backups ??= new List<BackupItem>();
        Backups.Insert(0, item);
    }

    /// <summary>
    /// Optional callback invoked before <see cref="TriggerUploadRequest"/> is called via the ribbon action.
    /// Used by <see cref="SetupCardViewModel"/> to expand the backup section before the upload dialog opens.
    /// </summary>
    internal Action? BeforeUploadCallback { get; set; }

    /// <summary>
    /// Triggers the <see cref="UploadRequested"/> event to instruct the UI to open a file picker.
    /// </summary>
    public void TriggerUploadRequest()
    {
        UploadRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Builds ribbon register definitions for backup related actions (create/upload).
    /// </summary>
    /// <param name="localizer">Localizer used to resolve UI labels.</param>
    /// <returns>Collection of ribbon register definitions or <c>null</c> when none are provided.</returns>
    protected override IReadOnlyList<UiRibbonRegister>? GetRibbonRegisterDefinition(IStringLocalizer localizer)
    {
        var groupTitle = localizer["SetupBackup_Titel"].Value;
        var actions = new List<UiRibbonAction>
        {
            new UiRibbonAction(
                "CreateBackup",
                localizer["Ribbon_CreateBackup"].Value,
                "<svg><use href='/icons/sprite.svg#save'/></svg>",
                UiRibbonItemSize.Large,
                false,
                localizer["Hint_CreateBackup"].Value ?? string.Empty,
                new Func<Task>(async () =>
                {
                    try { await CreateAsync(); }
                    catch (Exception ex) { _logger?.LogError(ex, "CreateBackup ribbon action failed"); }
                })),

            new UiRibbonAction(
                "UploadBackup",
                localizer["Ribbon_UploadBackup"].Value,
                "<svg><use href='/icons/sprite.svg#upload'/></svg>",
                UiRibbonItemSize.Large,
                false,
                localizer["Hint_UploadBackup"].Value ?? string.Empty,
                new Func<Task>(() =>
                {
                    try { BeforeUploadCallback?.Invoke(); }
                    catch (Exception ex) { _logger?.LogError(ex, "UploadBackup ribbon action failed"); }
                    return Task.CompletedTask;
                }))
        };

        var tabs = new List<UiRibbonTab>
        {
            new UiRibbonTab(groupTitle, actions, int.MaxValue)
        };

        return new List<UiRibbonRegister> { new UiRibbonRegister(UiRibbonRegisterKind.Actions, tabs) };
    }

    private static BackupItem MapToBackupItem(BackupDto dto)
        => new BackupItem { Id = dto.Id, CreatedUtc = dto.CreatedUtc, FileName = dto.FileName, SizeBytes = dto.SizeBytes, Source = dto.Source };

    private void BeginBusyOperation()
    {
        Busy = true;
        SetError(null, null);
        RaiseStateChanged();
    }

    private void HandleApiException(Exception ex)
    {
        SetError(ApiClient.LastErrorCode ?? null, ApiClient.LastError ?? ex.Message);
    }
}
