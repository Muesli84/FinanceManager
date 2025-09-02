using FinanceManager.Domain;

namespace FinanceManager.Application.Contacts;

public sealed record ContactDto(Guid Id, string Name, ContactType Type, Guid? CategoryId, string? Description, bool IsPaymentIntermediary);
