namespace FinanceManager.Shared.Dtos.Postings
{
    /// <summary>
    /// DTO representing a posting with extended metadata used by service endpoints and client view models.
    /// </summary>
    public sealed record PostingServiceDto(
        /// <summary>Unique posting identifier.</summary>
        Guid Id,
        /// <summary>Booking date of the posting.</summary>
        DateTime BookingDate,
        /// <summary>Valuta date of the posting.</summary>
        DateTime ValutaDate,
        /// <summary>Amount of the posting.</summary>
        decimal Amount,
        /// <summary>Kind/category of the posting.</summary>
        PostingKind Kind,
        /// <summary>Bank account id when applicable.</summary>
        Guid? AccountId,
        /// <summary>Contact id when applicable.</summary>
        Guid? ContactId,
        /// <summary>Savings plan id when applicable.</summary>
        Guid? SavingsPlanId,
        /// <summary>Security id when applicable.</summary>
        Guid? SecurityId,
        /// <summary>Original domain source id for traceability.</summary>
        Guid SourceId,
        /// <summary>Subject or title associated with the posting.</summary>
        string? Subject,
        /// <summary>Recipient or counterparty name.</summary>
        string? RecipientName,
        /// <summary>Optional description or additional details.</summary>
        string? Description,
        /// <summary>Security sub type (enum) for security-related postings.</summary>
        SecurityPostingSubType? SecuritySubType,
        /// <summary>Optional quantity for security-related postings.</summary>
        decimal? Quantity,
        /// <summary>Linked group id to connect related postings.</summary>
        Guid GroupId,
        /// <summary>Linked posting id when this posting has a counterpart.</summary>
        Guid? LinkedPostingId,
        /// <summary>Linked posting kind (enum) when linked.</summary>
        PostingKind? LinkedPostingKind,
        /// <summary>Linked posting account id, when applicable.</summary>
        Guid? LinkedPostingAccountId,
        /// <summary>Linked posting account symbol attachment id.</summary>
        Guid? LinkedPostingAccountSymbolAttachmentId,
        /// <summary>Linked posting account name.</summary>
        string? LinkedPostingAccountName,
        /// <summary>Bank posting account id for this posting, when available.</summary>
        Guid? BankPostingAccountId,
        /// <summary>Bank posting account symbol attachment id.</summary>
        Guid? BankPostingAccountSymbolAttachmentId,
        /// <summary>Bank posting account name.</summary>
        string? BankPostingAccountName);
}
