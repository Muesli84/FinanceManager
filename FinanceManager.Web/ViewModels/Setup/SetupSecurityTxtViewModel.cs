using FinanceManager.Shared.Dtos.Admin;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace FinanceManager.Web.ViewModels.Setup;

/// <summary>
/// View model for editing security.txt settings in the setup area.
/// </summary>
public sealed class SetupSecurityTxtViewModel : BaseViewModel
{
    private const string InvalidExpiresMessage = "Expires must be a valid date and time.";

    private readonly FinanceManager.Shared.IApiClient _api;
    private readonly ILogger<SetupSecurityTxtViewModel>? _logger;
    private SecurityTxtSettingsDto _original = new();
    private bool _busy;
    private bool _dirty;
    private bool _hasExpiresValidationError;

    /// <summary>Creates a new instance.</summary>
    public SetupSecurityTxtViewModel(IServiceProvider sp) : base(sp)
    {
        _api = sp.GetRequiredService<FinanceManager.Shared.IApiClient>();
        _logger = sp.GetService<ILogger<SetupSecurityTxtViewModel>>();
    }

    /// <summary>Current editable settings.</summary>
    /// <returns>The result.</returns>
    public SecurityTxtSettingsDto Model { get; private set; } = new();

    /// <summary>Editable expiry value in HTML datetime-local format.</summary>
    public string ExpiresText
    {
        get => Model.Expires.ToString("yyyy-MM-ddTHH:mm");
        set
        {
            if (DateTimeOffset.TryParse(value, out var dto))
            {
                Model.Expires = dto;
                _hasExpiresValidationError = false;
            }
            else
            {
                _hasExpiresValidationError = true;
                SaveError = InvalidExpiresMessage;
            }

            OnChanged();
        }
    }

    /// <summary>Indicates whether the last load/save operation is in progress.</summary>
    public bool Busy
    {
        get => _busy;
        private set
        {
            if (_busy == value)
            {
                return;
            }

            _busy = value;
            RaiseStateChanged();
        }
    }

    /// <summary>Indicates whether the current values differ from the last loaded snapshot.</summary>
    public bool Dirty
    {
        get => _dirty;
        private set
        {
            if (_dirty == value)
            {
                return;
            }

            _dirty = value;
            RaiseStateChanged();
        }
    }

    /// <summary>Last error message.</summary>
    public string? Error { get; private set; }

    /// <summary>Last save error message.</summary>
    public string? SaveError { get; private set; }

    /// <summary>True when the last save operation completed successfully.</summary>
    public bool SavedOk { get; private set; }

    /// <summary>Loads the current settings.</summary>
    /// <param name="ct">Cancellation token.</param>
    public async Task LoadAsync(CancellationToken ct = default)
    {
        Busy = true;
        Error = null;
        SaveError = null;
        SavedOk = false;
        RaiseStateChanged();

        try
        {
            Model = await _api.GetSecurityTxtSettingsAsync(ct) ?? new SecurityTxtSettingsDto();
            _original = Clone(Model);
            _hasExpiresValidationError = false;
            RecomputeDirty();
        }
        catch (HttpRequestException ex)
        {
            Error = _api.LastError ?? ex.Message;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException ex)
        {
            Error = ex.Message;
        }
        catch (InvalidOperationException ex)
        {
            _logger?.LogError(ex, "Loading security.txt settings failed.");
            throw;
        }
        catch (JsonException ex)
        {
            _logger?.LogError(ex, "Loading security.txt settings failed.");
            throw;
        }
        catch (NotSupportedException ex)
        {
            _logger?.LogError(ex, "Loading security.txt settings failed.");
            throw;
        }
        finally
        {
            Busy = false;
            RaiseStateChanged();
        }
    }

    /// <summary>Saves the current settings.</summary>
    /// <param name="ct">Cancellation token.</param>
    public async Task SaveAsync(CancellationToken ct = default)
    {
        if (_hasExpiresValidationError)
        {
            SavedOk = false;
            SaveError = InvalidExpiresMessage;
            RaiseStateChanged();
            return;
        }

        if (!Dirty)
        {
            return;
        }

        Busy = true;
        SaveError = null;
        SavedOk = false;
        RaiseStateChanged();

        try
        {
            await _api.UpdateSecurityTxtSettingsAsync(new SecurityTxtSettingsUpdateRequest(
                Model.Contact,
                Model.Expires,
                Model.Encryption,
                Model.Acknowledgments,
                Model.PreferredLanguages,
                Model.Policy,
                Model.Hiring,
                Model.Canonical), ct);

            _original = Clone(Model);
            SavedOk = true;
            RecomputeDirty();
        }
        catch (HttpRequestException ex)
        {
            SaveError = _api.LastError ?? ex.Message;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException ex)
        {
            SaveError = ex.Message;
        }
        catch (InvalidOperationException ex)
        {
            _logger?.LogError(ex, "Saving security.txt settings failed.");
            throw;
        }
        catch (JsonException ex)
        {
            _logger?.LogError(ex, "Saving security.txt settings failed.");
            throw;
        }
        catch (NotSupportedException ex)
        {
            _logger?.LogError(ex, "Saving security.txt settings failed.");
            throw;
        }
        finally
        {
            Busy = false;
            RaiseStateChanged();
        }
    }

    /// <summary>Marks the view model as changed.</summary>
    public void OnChanged()
    {
        SavedOk = false;
        if (!_hasExpiresValidationError)
        {
            SaveError = null;
        }

        RecomputeDirty();
        RaiseStateChanged();
    }

    private void RecomputeDirty()
    {
        Dirty = Model.Contact != _original.Contact
            || Model.Expires != _original.Expires
            || Model.Encryption != _original.Encryption
            || Model.Acknowledgments != _original.Acknowledgments
            || Model.PreferredLanguages != _original.PreferredLanguages
            || Model.Policy != _original.Policy
            || Model.Hiring != _original.Hiring
            || Model.Canonical != _original.Canonical;
    }

    private static SecurityTxtSettingsDto Clone(SecurityTxtSettingsDto src) => new()
    {
        Contact = src.Contact,
        Expires = src.Expires,
        Encryption = src.Encryption,
        Acknowledgments = src.Acknowledgments,
        PreferredLanguages = src.PreferredLanguages,
        Policy = src.Policy,
        Hiring = src.Hiring,
        Canonical = src.Canonical
    };
}
