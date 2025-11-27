using System.ComponentModel.DataAnnotations;

namespace FinanceManager.Shared.Dtos;

/// <summary>
/// Request payload to create a contact.
/// </summary>
public sealed record ContactCreateRequest(
    [Required, MinLength(2)] string Name,
    ContactType Type,
    Guid? CategoryId,
    string? Description,
    bool? IsPaymentIntermediary
);

/// <summary>
/// Request payload to update an existing contact.
/// </summary>
public sealed record ContactUpdateRequest(
    [Required, MinLength(2)] string Name,
    ContactType Type,
    Guid? CategoryId,
    string? Description,
    bool? IsPaymentIntermediary
);

/// <summary>
/// Request payload to create a new alias pattern.
/// </summary>
public sealed record AliasCreateRequest([Required, MinLength(1)] string Pattern);

/// <summary>
/// Request payload to merge the current contact into a target contact.
/// </summary>
public sealed record ContactMergeRequest([Required] Guid TargetContactId);
