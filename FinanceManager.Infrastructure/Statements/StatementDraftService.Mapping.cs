using System.Linq;
using FinanceManager.Domain.Statements;
using FinanceManager.Application.Statements;
using FinanceManager.Shared.Dtos;

namespace FinanceManager.Infrastructure.Statements;

public sealed partial class StatementDraftService
{
    private static StatementDraftDto Map(StatementDraft draft)
    {
        var total = draft.Entries.Sum(e => e.Amount);
        return new StatementDraftDto(
            draft.Id,
            draft.OriginalFileName,
            draft.Description,
            draft.DetectedAccountId,
            draft.Status,
            total,
            false,
            null,
            null,
            null,
            draft.UploadGroupId,
            draft.Entries.Select(e => Map(e)).ToList());
    }

    private static StatementDraftEntryDto Map(StatementDraftEntry e)
    {
        return new StatementDraftEntryDto(
            e.Id,
            e.BookingDate,
            e.ValutaDate,
            e.Amount,
            e.CurrencyCode,
            e.Subject,
            e.RecipientName,
            e.BookingDescription,
            e.IsAnnounced,
            e.IsCostNeutral,
            e.Status,
            e.ContactId,
            e.SavingsPlanId,
            e.ArchiveSavingsPlanOnBooking,
            e.SplitDraftId,
            e.SecurityId,
            e.SecurityTransactionType,
            e.SecurityQuantity,
            e.SecurityFeeAmount,
            e.SecurityTaxAmount);
    }

    private static StatementDraftDto Map(StatementDraft draft, IDictionary<Guid, dynamic> splitRefLookup)
    {
        var total = draft.Entries.Sum(e => e.Amount);
        dynamic? refInfo = null;
        splitRefLookup.TryGetValue(draft.Id, out refInfo);
        Guid? parentDraftId = refInfo?.DraftId;
        Guid? parentEntryId = refInfo?.Id;
        decimal? parentEntryAmount = refInfo?.Amount;

        return new StatementDraftDto(
            draft.Id,
            draft.OriginalFileName,
            draft.Description,
            draft.DetectedAccountId,
            draft.Status,
            total,
            parentDraftId != null,
            parentDraftId,
            parentEntryId,
            parentEntryAmount,
            draft.UploadGroupId,
            draft.Entries.Select(e => new StatementDraftEntryDto(
                e.Id,
                e.BookingDate,
                e.ValutaDate,
                e.Amount,
                e.CurrencyCode,
                e.Subject,
                e.RecipientName,
                e.BookingDescription,
                e.IsAnnounced,
                e.IsCostNeutral,
                e.Status,
                e.ContactId,
                e.SavingsPlanId,
                e.ArchiveSavingsPlanOnBooking,
                e.SplitDraftId,
                e.SecurityId,
                e.SecurityTransactionType,
                e.SecurityQuantity,
                e.SecurityFeeAmount,
                e.SecurityTaxAmount)).ToList());
    }
}
