namespace FinanceManager.Web.ViewModels.Common;

/// <summary>
/// Size of a ribbon item shown in the UI. Used to control visual prominence of actions.
/// </summary>
public enum UiRibbonItemSize
{
    /// <summary>Smaller action item suitable for secondary actions.</summary>
    Small,
    /// <summary>Larger action item suitable for primary actions.</summary>
    Large
}

/// <summary>
/// Kind of ribbon register. Registers group tabs logically (e.g. actions, quick access).
/// </summary>
public enum UiRibbonRegisterKind
{
    /// <summary>Register shown in quick access area.</summary>
    QuickAccess,
    /// <summary>Register that contains the main set of actions for the current view.</summary>
    Actions,
    /// <summary>Register for linked information related actions.</summary>
    LinkedInfo,
    /// <summary>Register used to expose report related actions.</summary>
    Reports,
    /// <summary>Custom register kind for extensions.</summary>
    Custom
}

/// <summary>
/// Represents a single action that can be shown in a ribbon tab.
/// </summary>
/// <param name="Id">Unique identifier for the action.</param>
/// <param name="Label">Localized label for the action.</param>
/// <param name="IconSvg">Inline SVG markup used as icon.</param>
/// <param name="Size">Visual size of the action item (<see cref="UiRibbonItemSize"/>).</param>
/// <param name="Disabled">Whether the action should be rendered disabled.</param>
/// <param name="Tooltip">Optional tooltip text.</param>
/// <param name="Callback">Callback executed when the action is invoked.</param>
/// <returns>The result.</returns>
public sealed record UiRibbonAction(
    string Id,
    string Label,
    string IconSvg,
    UiRibbonItemSize Size,
    bool Disabled,
    string? Tooltip,
    Func<Task>? Callback
)
{
    /// <summary>
    /// Compatibility alias for older code/tests that referenced <c>Action</c> instead of <c>Id</c>.
    /// </summary>
    public string Action => Id;

    /// <summary>
    /// Optional file-change callback used for import-type actions. When set this callback is invoked
    /// with the <see cref="Microsoft.AspNetCore.Components.Forms.InputFileChangeEventArgs"/> when a file is selected.
    /// Kept as init-only for backwards compatibility with the positional constructor.
    /// </summary>
    public Func<Microsoft.AspNetCore.Components.Forms.InputFileChangeEventArgs, System.Threading.Tasks.Task>? FileCallback { get; init; }

    /// <summary>
    /// When <c>true</c> the action is rendered as an icon-only shortcut in the closed mobile ribbon group header.
    /// The shortcut invokes the same callback as the regular ribbon action.
    /// </summary>
    public bool MobileShortcut { get; init; }

    /// <summary>
    /// When <c>true</c> the action is completely omitted from the rendered ribbon.
    /// Use this to hide context-sensitive buttons that should not be visible in certain states
    /// (e.g. hiding the "Start quick-edit" button while quick-edit is already active).
    /// </summary>
    public bool Hidden { get; init; }
};

/// <summary>
/// Represents a single ribbon tab containing a title and a collection of actions.
/// </summary>
/// <param name="Title">Tab display title.</param>
/// <param name="Items">Actions contained in the tab.</param>
/// <param name="Sort">Optional sort index used by consumers to order tabs.</param>
/// <returns>The result.</returns>
public sealed record UiRibbonTab(string Title, List<UiRibbonAction> Items, int Sort = 0);

/// <summary>
/// Grouping of ribbon tabs into a register of a specific kind (e.g. Actions, QuickAccess).
/// </summary>
/// <param name="Kind">Kind of the register.</param>
/// <param name="Tabs">Tabs contained in this register; may be <c>null</c> for empty registers.</param>
/// <returns>The result.</returns>
public sealed record UiRibbonRegister(UiRibbonRegisterKind Kind, List<UiRibbonTab>? Tabs)
{
    /// <summary>
    /// Helper property exposing the title of the first tab for compatibility with older callers that expect a flat shape.
    /// Returns an empty string when no tabs are present.
    /// </summary>
    /// <returns>The result.</returns>
    public string Title => (Tabs != null && Tabs.Count > 0) ? Tabs[0].Title : string.Empty;

    /// <summary>
    /// Helper property exposing the items of the first tab for compatibility with older callers that expect a flat shape.
    /// Returns an empty list when no tabs are present.
    /// </summary>
    /// <returns>The result.</returns>
    public List<UiRibbonAction> Items => (Tabs != null && Tabs.Count > 0) ? Tabs[0].Items : new List<UiRibbonAction>();
}

/// <summary>
/// Legacy compatibility type representing a flattened ribbon item used by older viewmodels/components.
/// </summary>
/// <param name="Label">Display label of the item.</param>
/// <param name="IconSvg">SVG markup used as icon.</param>
/// <param name="Size">Size hint for the item.</param>
/// <param name="Disabled">Whether the item is disabled.</param>
/// <param name="Action">Action identifier used to identify the item when invoked.</param>
/// <param name="Tooltip">Optional tooltip text.</param>
/// <param name="Callback">Optional callback invoked when the item is activated.</param>
/// <returns>The result.</returns>
public sealed record UiRibbonItem(
    string Label,
    string IconSvg,
    UiRibbonItemSize Size,
    bool Disabled,
    string Action,
    string? Tooltip = null,
    Func<Task>? Callback = null
);

/// <summary>
/// Represents a compatibility group containing a title and a collection of <see cref="UiRibbonItem"/> entries.
/// </summary>
/// <param name="Title">Group title.</param>
/// <param name="Items">Items included in the group.</param>
/// <returns>The result.</returns>
public sealed record UiRibbonGroup(string Title, List<UiRibbonItem> Items);
