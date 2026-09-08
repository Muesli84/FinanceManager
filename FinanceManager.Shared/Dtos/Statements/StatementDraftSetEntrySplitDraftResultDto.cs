using System;
using FinanceManager.Shared.Dtos.Statements;

namespace FinanceManager.Shared.Dtos.Statements
{
    /// <summary>
    /// Result payload for assigning/clearing a split draft on an entry.
    /// </summary>
    /// <param name="Entry">The entry.</param>
    /// <param name="SplitSum">The split sum.</param>
    /// <param name="Difference">The difference.</param>
    /// <returns>The result.</returns>
    public sealed record StatementDraftSetEntrySplitDraftResultDto(StatementDraftEntryDto Entry, decimal? SplitSum, decimal? Difference);
}
