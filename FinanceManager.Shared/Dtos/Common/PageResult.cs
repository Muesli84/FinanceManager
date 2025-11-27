namespace FinanceManager.Shared.Dtos.Common;

/// <summary>
/// DTO representing a paged result envelope.
/// </summary>
/// <typeparam name="T">Type of the items contained in the page.</typeparam>
public sealed class PageResult<T>
{
    /// <summary>List of items contained in the current page.</summary>
    public List<T> Items { get; set; } = new();
    /// <summary>True when more items are available beyond this page.</summary>
    public bool HasMore { get; set; }
    /// <summary>Total number of items matching the query.</summary>
    public int? Total { get; set; }
}
