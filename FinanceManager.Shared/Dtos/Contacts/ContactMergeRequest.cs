using System.ComponentModel.DataAnnotations;

namespace FinanceManager.Shared.Dtos.Contacts;

/// <summary>
/// Strategy for handling competing values when merging two contacts.
/// </summary>
public enum MergePreference
{
    /// <summary>
    /// Prefer values from the destination (target) contact when conflicts occur.
    /// </summary>
    DestinationFirst,

    /// <summary>
    /// Prefer values from the source (current) contact when conflicts occur.
    /// </summary>
    SourceFirst
}

/// <summary>
/// Request payload to merge the current contact into a target contact.
/// </summary>
/// <param name="TargetContactId">The identifier of the target contact to merge into.</param>
/// <param name="Preference">Preference strategy for resolving conflicting values.</param>
/// <returns>The result.</returns>
public sealed record ContactMergeRequest([Required] Guid TargetContactId, MergePreference Preference = MergePreference.DestinationFirst);
