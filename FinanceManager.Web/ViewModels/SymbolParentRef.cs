using FinanceManager.Domain.Attachments;

namespace FinanceManager.Web.ViewModels;

/// <summary>
/// Reference to the attachment parent entity used for symbol uploads.
/// </summary>
/// <param name="Kind">The attachment entity kind of the parent.</param>
/// <param name="ParentId">The id of the parent entity.</param>
/// <returns>The result.</returns>
public readonly record struct SymbolParentRef(AttachmentEntityKind Kind, Guid ParentId);
