namespace FinanceManager.Shared.Dtos.Postings
{
    public sealed record PostingServiceDto(
        Guid Id,
        DateTime BookingDate,
        DateTime ValutaDate,
        decimal Amount,
        PostingKind Kind,
        Guid? AccountId,
        Guid? ContactId,
        Guid? SavingsPlanId,
        Guid? SecurityId,
        Guid SourceId,
        string? Subject,
        string? RecipientName,
        string? Description,
        int? SecuritySubType,
        decimal? Quantity,
        Guid GroupId,
        Guid? LinkedPostingId,
        int? LinkedPostingKind,
        Guid? LinkedPostingAccountId,
        Guid? LinkedPostingAccountSymbolAttachmentId,
        string? LinkedPostingAccountName,
        Guid? BankPostingAccountId,
        Guid? BankPostingAccountSymbolAttachmentId,
        string? BankPostingAccountName);
}
