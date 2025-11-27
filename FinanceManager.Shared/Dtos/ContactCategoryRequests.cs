using System.ComponentModel.DataAnnotations;

namespace FinanceManager.Shared.Dtos;

/// <summary>
/// Request payload to create a contact category.
/// </summary>
public sealed record ContactCategoryCreateRequest([Required, MinLength(2)] string Name);

/// <summary>
/// Request payload to update a contact category's name.
/// </summary>
public sealed record ContactCategoryUpdateRequest([Required, MinLength(2)] string Name);
