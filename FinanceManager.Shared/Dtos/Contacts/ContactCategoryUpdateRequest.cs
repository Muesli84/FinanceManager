using System.ComponentModel.DataAnnotations;

namespace FinanceManager.Shared.Dtos.Contacts;

/// <summary>
/// Request payload to rename a contact category.
/// </summary>
/// <param name="Name">New display name of the category.</param>
public sealed record ContactCategoryUpdateRequest([Required, MinLength(2)] string Name);
