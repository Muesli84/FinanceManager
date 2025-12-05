namespace FinanceManager.Shared.Dtos.Statements;

/// <summary>
/// Result object returned from validation containing all messages.
/// </summary>
/// <param name="DraftId">Validated draft id.</param>
/// <param name="IsValid">True when no errors were found.</param>
/// <param name="Messages">List of validation messages.</param>
public sealed record DraftValidationResultDto(Guid DraftId, bool IsValid, IReadOnlyList<DraftValidationMessageDto> Messages);
