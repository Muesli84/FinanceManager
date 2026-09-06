namespace FinanceManager.Web.ViewModels.Common
{
    /// <summary>
    /// Kind of content that can be rendered in a list cell.
    /// </summary>
    public enum ListCellKind
    {
        /// <summary>
        /// Plain text content.
        /// </summary>
        Text,

        /// <summary>
        /// Symbol rendered from an attachment id.
        /// </summary>
        Symbol,

        /// <summary>
        /// Currency amount rendered using the current UI culture.
        /// </summary>
        Currency
    }

    /// <summary>
    /// Horizontal alignment options for list columns.
    /// </summary>
    public enum ListColumnAlign
    {
        /// <summary>
        /// Align content to the left.
        /// </summary>
        Left,

        /// <summary>
        /// Center content horizontally.
        /// </summary>
        Center,

        /// <summary>
        /// Align content to the right.
        /// </summary>
        Right
    }

    /// <summary>
    /// Describes a single column in a list view including key, title and optional width and alignment.
    /// </summary>
    /// <param name="Key">Unique key identifying the column.</param>
    /// <param name="Title">Localized column header text.</param>
    /// <param name="Width">Optional CSS width (for example "18%" or "48px").</param>
    /// <param name="Align">Horizontal alignment for cell content.</param>
    /// <returns>The result.</returns>
    public sealed record ListColumn(string Key, string Title, string? Width = null, ListColumnAlign Align = ListColumnAlign.Left);

    /// <summary>
    /// Represents a single cell in a list record. Depending on <see cref="Kind"/> different properties are used:
    /// Text for <see cref="ListCellKind.Text"/>, SymbolId for <see cref="ListCellKind.Symbol"/>, and Amount for <see cref="ListCellKind.Currency"/>.
    /// </summary>
    /// <param name="Kind">The kind of the cell which determines which value is displayed.</param>
    /// <param name="Text">Optional text to display for text cells.</param>
    /// <param name="SymbolId">Optional attachment id used for symbol cells.</param>
    /// <param name="Amount">Optional currency amount for currency cells.</param>
    /// <param name="IconUrl">Optional URL to an icon to display instead of a symbol attachment.</param>
    /// <param name="Muted">When true the cell should be rendered in a muted (grayed-out) style.</param>
    /// <returns>The result.</returns>
    public sealed record ListCell(ListCellKind Kind, string? Text = null, System.Guid? SymbolId = null, decimal? Amount = null, string? IconUrl = null, bool Muted = false);

    /// <summary>
    /// Describes how a row inside a mobile list card is rendered.
    /// </summary>
    public enum ListMobileRowKind
    {
        /// <summary>
        /// Render each mobile cell as a regular label/value row.
        /// </summary>
        Single,

        /// <summary>
        /// Render the mobile cells next to each other in two equal columns.
        /// </summary>
        TwoColumn
    }

    /// <summary>
    /// Represents a cell inside a mobile list-card row.
    /// </summary>
    /// <param name="Label">Optional label rendered above the value.</param>
    /// <param name="Cell">Cell value using the same rendering rules as regular list cells.</param>
    /// <param name="CssClass">Optional CSS class for this mobile cell.</param>
    /// <returns>The result.</returns>
    public sealed record ListMobileCell(string? Label, ListCell Cell, string? CssClass = null);

    /// <summary>
    /// Represents a row block inside a mobile list card.
    /// </summary>
    /// <param name="Cells">Cells rendered inside this mobile row.</param>
    /// <param name="Kind">Layout kind used for the row.</param>
    /// <param name="CssClass">Optional CSS class for this mobile row.</param>
    /// <returns>The result.</returns>
    public sealed record ListMobileRow(IReadOnlyList<ListMobileCell> Cells, ListMobileRowKind Kind = ListMobileRowKind.Single, string? CssClass = null);

    /// <summary>
    /// A single list record containing the rendered cells and the underlying item payload.
    /// </summary>
    /// <param name="Cells">Sequence of cells to render for the record.</param>
    /// <param name="Item">Optional underlying item object associated with the row (used for navigation or actions).</param>
    /// <param name="Hint">Optional hint text displayed as a full-width row under the record.</param>
    /// <param name="MobileRows">Optional specialized mobile card rows. When omitted the generic mobile fallback is used.</param>
    /// <returns>The result.</returns>
    public sealed record ListRecord(IReadOnlyList<ListCell> Cells, object? Item = null, string? Hint = null, IReadOnlyList<ListMobileRow>? MobileRows = null);

    /// <summary>
    /// Represents the different kinds of card fields that can be rendered in a card view.
    /// </summary>
    public enum CardFieldKind
    {
        /// <summary>
        /// A simple text field.
        /// </summary>
        Text,

        /// <summary>
        /// A date field.
        /// </summary>
        Date,

        /// <summary>
        /// A boolean (checkbox) field.
        /// </summary>
        Boolean,

        /// <summary>
        /// A symbol field backed by an attachment id.
        /// </summary>
        Symbol,

        /// <summary>
        /// A currency/amount field.
        /// </summary>
        Currency
    }

    /// <summary>
    /// Model describing a single field rendered on a card. Contains label key, display values and editing/lookup hints.
    /// </summary>
    public sealed class CardField
    {
        /// <summary>
        /// Localized label resource key for the field (e.g. "Card_Caption_Account_Name").
        /// </summary>
        public string LabelKey { get; set; }

        /// <summary>
        /// The kind of the field which controls rendering and editing widgets.
        /// </summary>
        public CardFieldKind Kind { get; set; }

        /// <summary>
        /// Textual representation of the field value (used for text/date/lookup fields).
        /// </summary>
        public string? Text { get; set; }

        /// <summary>
        /// Attachment id used when the field is a symbol.
        /// </summary>
        public System.Guid? SymbolId { get; set; }

        /// <summary>
        /// Numeric amount used when the field is a currency value.
        /// </summary>
        public decimal? Amount { get; set; }

        /// <summary>
        /// Boolean value used when the field kind is Boolean.
        /// </summary>
        public bool? BoolValue { get; set; }

        /// <summary>
        /// Indicates whether the field is editable in the UI.
        /// </summary>
        public bool Editable { get; set; }

        /// <summary>
        /// Lookup type string used to configure lookup editors (e.g. "Contact", "Enum:AccountType").
        /// </summary>
        public string? LookupType { get; set; }

        /// <summary>
        /// Field name from the lookup entity that should be displayed in the lookup results (usually "Name").
        /// </summary>
        public string? LookupField { get; set; }

        /// <summary>
        /// Optional free-form filter hint passed to lookup implementations (for example "Type=Bank").
        /// </summary>
        public string? LookupFilter { get; set; }

        /// <summary>
        /// Optional hint text displayed below the field spanning the full width of the card.
        /// </summary>
        public string? Hint { get; set; }

        /// <summary>
        /// Optional identifier value associated with the field (for lookups and symbol fields).
        /// </summary>
        public System.Guid? ValueId { get; set; }

        /// <summary>
        /// When true render an Add/New button next to lookup inputs so the user can create a new related entity.
        /// </summary>
        public bool AllowAdd { get; set; }

        /// <summary>
        /// Optional suggestion used when creating a new record from this lookup (for example to prefill the name field).
        /// </summary>
        public string? RecordCreationNameSuggestion { get; set; }

        /// <summary>
        /// Creates a new instance of <see cref="CardField"/>.
        /// </summary>
        /// <param name="labelKey">Label resource key.</param>
        /// <param name="kind">Field kind.</param>
        /// <param name="text">Optional display text.</param>
        /// <param name="symbolId">Optional symbol attachment id.</param>
        /// <param name="amount">Optional currency amount.</param>
        /// <param name="boolValue">Optional boolean value.</param>
        /// <param name="editable">Indicates whether the field is editable.</param>
        /// <param name="lookupType">Optional lookup type identifier.</param>
        /// <param name="lookupField">Optional lookup field to display.</param>
        /// <param name="valueId">Optional associated identifier (lookup selection or symbol id).</param>
        /// <param name="lookupFilter">Optional lookup filter hint.</param>
        /// <param name="hint">Optional hint text displayed under the field.</param>
        /// <param name="allowAdd">Whether to show an Add/New control for lookup fields.</param>
        /// <param name="recordCreationNameSuggestion">Optional suggestion used when creating new records from this lookup.</param>
        public CardField(string labelKey, CardFieldKind kind, string? text = null, System.Guid? symbolId = null, decimal? amount = null, bool? boolValue = null, bool editable = false, string? lookupType = null, string? lookupField = null, System.Guid? valueId = null, string? lookupFilter = null, string? hint = null, bool allowAdd = false, string? recordCreationNameSuggestion = null)
        {
            LabelKey = labelKey;
            Kind = kind;
            Text = text;
            SymbolId = symbolId;
            Amount = amount;
            BoolValue = boolValue;
            Editable = editable;
            LookupType = lookupType;
            LookupField = lookupField;
            LookupFilter = lookupFilter;
            ValueId = valueId;
            Hint = hint;
            AllowAdd = allowAdd;
            RecordCreationNameSuggestion = recordCreationNameSuggestion;
        }
    }

    /// <summary>
    /// Represents the model used to render a card composed of multiple card fields.
    /// </summary>
    /// <param name="Fields">Fields to render on the card in display order.</param>
    /// <param name="Item">Optional underlying item payload associated with the card.</param>
    /// <returns>The result.</returns>
    public sealed record CardRecord(IReadOnlyList<CardField> Fields, object? Item = null);
}
