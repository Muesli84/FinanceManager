using System.ComponentModel.DataAnnotations;

namespace FinanceManager.Shared.Dtos;

public sealed record ContactCategoryCreateRequest([Required, MinLength(2)] string Name);
public sealed record ContactCategoryUpdateRequest([Required, MinLength(2)] string Name);
