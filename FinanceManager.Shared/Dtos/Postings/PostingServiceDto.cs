namespace FinanceManager.Shared.Dtos.Postings
{
    /// <summary>
    /// DTO representing a posting with extended metadata used by service endpoints and client view models.
    /// </summary>
    /// <param name="Id">Unique posting identifier.</param>
    /// <param name="BookingDate">Booking date of the posting.</param>
    /// <param name="ValutaDate">Valuta date of the posting.</param>
    /// <param name="Amount">Amount of the posting.</param>
    /// <param name="Kind">Kind/category of the posting.</param>
    /// <param name="AccountId">Bank account id when applicable.</param>
    /// <param name="ContactId">Contact id when applicable.</param>
    /// <param name="SavingsPlanId">Savings plan id when applicable.</param>
    /// <param name="SecurityId">Security id when applicable.</param>
    /// <param name="SourceId">Original domain source id for traceability.</param>
    /// <param name="Subject">Subject or title associated with the posting.</param>
    /// <param name="RecipientName">Recipient or counterparty name.</param>
    /// <param name="Description">Optional description or additional details.</param>
    /// <param name="SecuritySubType">Security sub type (enum) for security-related postings.</param>
    /// <param name="Quantity">Optional quantity for security-related postings.</param>
    /// <param name="GroupId">Linked group id to connect related postings.</param>
    /// <param name="LinkedPostingId">Linked posting id when this posting has a counterpart.</param>
    /// <param name="LinkedPostingKind">Linked posting kind (enum) when linked.</param>
    /// <param name="LinkedPostingAccountId">Linked posting account id, when applicable.</param>
    /// <param name="LinkedPostingAccountSymbolAttachmentId">Linked posting account symbol attachment id.</param>
    /// <param name="LinkedPostingAccountName">Linked posting account name.</param>
    /// <param name="BankPostingAccountId">Bank posting account id for this posting, when available.</param>
    /// <param name="BankPostingAccountSymbolAttachmentId">Bank posting account symbol attachment id.</param>
    /// <param name="BankPostingAccountName">Bank posting account name.</param>
    /// <param name="IsReversed">Indicates whether this posting has been reversed by a counter-posting.</param>
    /// <param name="IsReversal">Indicates whether this posting is itself a reversal (counter-posting).</param>
    /// <param name="ReversedByPostingId">Id of the reversal posting that reversed this posting; populated when <see cref="IsReversed"/> is <c>true</c>.</param>
    /// <param name="ReversalForPostingId">Id of the original posting that this posting reverses; populated when <see cref="IsReversal"/> is <c>true</c>.</param>
    /// <param name="IsPreliminary">Indicates whether this posting is a preliminary (provisional) booking.</param>
    /// <returns>The result.</returns>
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
        SecurityPostingSubType? SecuritySubType,
        decimal? Quantity,
        Guid GroupId,
        Guid? LinkedPostingId,
        PostingKind? LinkedPostingKind,
        Guid? LinkedPostingAccountId,
        Guid? LinkedPostingAccountSymbolAttachmentId,
        string? LinkedPostingAccountName,
        Guid? BankPostingAccountId,
        Guid? BankPostingAccountSymbolAttachmentId,
        string? BankPostingAccountName,
        bool IsReversed,
        bool IsReversal,
        Guid? ReversedByPostingId,
        Guid? ReversalForPostingId,
        bool IsPreliminary = false);
}
