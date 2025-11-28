using System.ComponentModel.DataAnnotations;

namespace FinanceManager.Shared.Dtos.Contacts;

/// <summary>
/// Request payload to create a new alias pattern for contact classification.
/// </summary>
/// <param name="Pattern">The alias pattern to add.</param>
public sealed record AliasCreateRequest([Required, MinLength(1)] string Pattern);
