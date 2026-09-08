using System.ComponentModel.DataAnnotations;

namespace FinanceManager.Shared.Dtos.Contacts;

/// <summary>
/// Request payload to update an existing contact.
/// </summary>
/// <param name="Name">The name.</param>
/// <param name="Type">The type.</param>
/// <param name="CategoryId">The category id.</param>
/// <param name="Description">The description.</param>
/// <param name="IsPaymentIntermediary">The is payment intermediary.</param>
/// <returns>The result.</returns>
public sealed record ContactUpdateRequest(
    [Required, MinLength(2)] string Name,
    ContactType Type,
    Guid? CategoryId,
    string? Description,
    bool? IsPaymentIntermediary
);
