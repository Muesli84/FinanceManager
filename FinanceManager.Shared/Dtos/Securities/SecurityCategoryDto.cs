namespace FinanceManager.Shared.Dtos.Securities;

/// <summary>
/// DTO describing a security category used to group securities.
/// </summary>
public sealed class SecurityCategoryDto
{
    /// <summary>Unique category identifier.</summary>
    public Guid Id { get; set; }
    /// <summary>Display name of the category.</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Optional symbol attachment id associated with the category.</summary>
    public Guid? SymbolAttachmentId { get; set; }
}