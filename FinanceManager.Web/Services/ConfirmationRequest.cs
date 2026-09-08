namespace FinanceManager.Web.Services;

/// <summary>
/// Describes a confirmation request that is displayed by the shared confirmation dialog.
/// All user-facing text is referenced by resource keys so localization is handled centrally.
/// </summary>
/// <param name="TitleResourceKey">Resource key for the dialog title.</param>
/// <param name="MessageResourceKey">Resource key for the dialog message.</param>
/// <param name="ContextId">Optional context identifier (e.g. entity id or action name) for logging or diagnostics.</param>
/// <param name="Severity">Visual severity of the confirmation.</param>
/// <param name="ConfirmButtonResourceKey">Optional resource key for the confirm button label; falls back to <c>Btn_Confirm</c>.</param>
/// <returns>A confirmation request.</returns>
public sealed record ConfirmationRequest(
    string TitleResourceKey,
    string MessageResourceKey,
    string? ContextId = null,
    ConfirmationSeverity Severity = ConfirmationSeverity.Default,
    string? ConfirmButtonResourceKey = null);
