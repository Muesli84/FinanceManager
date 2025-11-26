using System.ComponentModel.DataAnnotations;

namespace FinanceManager.Shared.Dtos;

public sealed record ContactCreateRequest(
    [Required, MinLength(2)] string Name,
    ContactType Type,
    Guid? CategoryId,
    string? Description,
    bool? IsPaymentIntermediary
);

public sealed record ContactUpdateRequest(
    [Required, MinLength(2)] string Name,
    ContactType Type,
    Guid? CategoryId,
    string? Description,
    bool? IsPaymentIntermediary
);

public sealed record AliasCreateRequest([Required, MinLength(1)] string Pattern);

public sealed record ContactMergeRequest([Required] Guid TargetContactId);
