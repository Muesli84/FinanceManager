namespace FinanceManager.Domain.Attachments;

public enum AttachmentEntityKind : short
{
    StatementDraftEntry = 0,
    StatementEntry = 1,
    Contact = 2,
    SavingsPlan = 3,
    Security = 4,
    Account = 5,
    StatementImport = 6,
    Posting = 7,
    StatementDraft = 8, // newly added for original uploaded statement files per draft
    ContactCategory = 9, // symbol for contact category
    SavingsPlanCategory = 10, // symbol for savings plan category
    SecurityCategory = 11 // symbol for security category
}
