namespace FinanceManager.Web.Services;

/// <summary>
/// Central service for requesting user confirmation before destructive or irreversible actions.
/// When the user has disabled confirmation dialogs, <see cref="ConfirmAsync"/> returns <c>true</c> immediately.
/// </summary>
public interface IConfirmationService
{
    /// <summary>
    /// Raised when a confirmation dialog should be shown.
    /// </summary>
    event EventHandler<ConfirmationRequest>? OnShow;

    /// <summary>
    /// Raised whenever the pending confirmation request changes (show, confirm, cancel, or dismiss).
    /// UI hosts should refresh their rendering on this event.
    /// </summary>
    event EventHandler? OnChanged;

    /// <summary>
    /// Gets the currently pending confirmation request, if any.
    /// </summary>
    ConfirmationRequest? CurrentRequest { get; }

    /// <summary>
    /// Requests confirmation for an action.
    /// Returns <c>true</c> immediately if the user has disabled confirmation dialogs or if the user confirms.
    /// Returns <c>false</c> if the user cancels.
    /// </summary>
    /// <param name="request">The confirmation request to display.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><c>true</c> when the action should proceed; otherwise <c>false</c>.</returns>
    Task<bool> ConfirmAsync(ConfirmationRequest request, CancellationToken ct = default);

    /// <summary>
    /// Sets the result of the current confirmation. Must be called by the UI when the user confirms or cancels.
    /// </summary>
    /// <param name="confirmed"><c>true</c> when the user confirmed; <c>false</c> when cancelled.</param>
    void SetResult(bool confirmed);

    /// <summary>
    /// Cancels the current confirmation (equivalent to <see cref="SetResult(bool)"/> with <c>false</c>).
    /// </summary>
    void Cancel();

    /// <summary>
    /// Invalidates the cached user preference so the next confirmation re-reads the current setting.
    /// </summary>
    Task InvalidateCacheAsync();
}
