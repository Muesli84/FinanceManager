namespace FinanceManager.Shared.Dtos;

public sealed class PageResult<T>
{
    public List<T> Items { get; set; } = new();
    public bool HasMore { get; set; }
    public int? Total { get; set; }
}
