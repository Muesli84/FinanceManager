using System.ComponentModel.DataAnnotations;

namespace FinanceManager.Shared.Dtos.Contacts;

/// <summary>
/// Request payload to create a contact category.
/// </summary>
/// <param name="Name">The name.</param>
/// <returns>The result.</returns>
public sealed record ContactCategoryCreateRequest([Required, MinLength(2)] string Name);
