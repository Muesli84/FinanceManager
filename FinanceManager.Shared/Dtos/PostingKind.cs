namespace FinanceManager.Shared.Dtos
{
    /// <summary>
    /// Defines the kind/category of a posting.
    /// </summary>
    public enum PostingKind
    {
        /// <summary>Bank account posting.</summary>
        Bank = 0,
        /// <summary>Contact posting.</summary>
        Contact = 1,
        /// <summary>Savings plan posting.</summary>
        SavingsPlan = 2,
        /// <summary>Security posting.</summary>
        Security = 3
    }
}
