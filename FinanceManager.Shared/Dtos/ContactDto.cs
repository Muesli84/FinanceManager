namespace FinanceManager.Shared.Dtos;

public enum ContactType
{
    Self = 0,
    Bank = 1,
    Person = 2,
    Organization = 3,
    Other = 9
}
public sealed record ContactDto(Guid Id, string Name, ContactType Type, Guid? CategoryId, string? Description, bool IsPaymentIntermediary, Guid? SymbolAttachmentId);
