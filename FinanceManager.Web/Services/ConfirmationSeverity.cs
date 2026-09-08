namespace FinanceManager.Web.Services;

/// <summary>
/// Indicates the visual severity of a confirmation dialog.
/// </summary>
public enum ConfirmationSeverity
{
    /// <summary>Default confirmation, no special emphasis.</summary>
    Default,

    /// <summary>Warning confirmation, indicates a potentially impactful action.</summary>
    Warning,

    /// <summary>Critical confirmation, indicates a destructive or irreversible action.</summary>
    Critical
}
