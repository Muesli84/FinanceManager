using System.ComponentModel.DataAnnotations;

namespace FinanceManager.Shared.Dtos.Contacts;

/// <summary>
/// Request payload to create a contact category.
/// </summary>
public sealed record ContactCategoryCreateRequest([Required, MinLength(2)] string Name);
