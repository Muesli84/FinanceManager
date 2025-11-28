using System.ComponentModel.DataAnnotations;

namespace FinanceManager.Shared.Dtos.Contacts;

/// <summary>
/// Request payload to merge the current contact into a target contact.
/// </summary>
public sealed record ContactMergeRequest([Required] Guid TargetContactId);
