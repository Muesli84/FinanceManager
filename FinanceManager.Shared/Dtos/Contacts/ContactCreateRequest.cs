using System.ComponentModel.DataAnnotations;

namespace FinanceManager.Shared.Dtos.Contacts;

/// <summary>
/// Request payload to create a new contact.
/// </summary>
/// <param name="Name">Display name of the contact.</param>
/// <param name="Type">Type/category of the contact.</param>
/// <param name="CategoryId">Optional contact category identifier.</param>
/// <param name="Description">Optional description for the contact.</param>
/// <param name="IsPaymentIntermediary">True when the contact is a payment intermediary.</param>
public sealed record ContactCreateRequest(
    [Required, MinLength(2)] string Name,
    ContactType Type,
    Guid? CategoryId,
    string? Description,
    bool? IsPaymentIntermediary
);
