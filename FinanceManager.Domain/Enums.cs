namespace FinanceManager.Domain;

public enum AccountType
{
    Giro = 0,
    Savings = 1
}

public enum ContactType
{
    Self = 0,
    Bank = 1,
    Person = 2,
    Organization = 3,
    Other = 9
}

public enum AccountShareRole
{
    Read = 0,
    Write = 1,
    Admin = 2
}

public enum ImportFormat
{
    Csv = 0,
    Pdf = 1
}

public enum StatementEntryStatus
{
    Pending = 0,
    Booked = 1,
    IgnoredDuplicate = 2
}

public enum StatementDraftStatus
{
    Draft = 0,
    Committed = 1,
    Expired = 2
}

public enum PostingKind
{
    Bank = 0,
    Contact = 1,
    SavingsPlan = 2,
    Security = 3
}
