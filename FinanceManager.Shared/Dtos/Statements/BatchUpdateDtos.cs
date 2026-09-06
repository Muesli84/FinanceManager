using System.Collections.Generic;
using System;

namespace FinanceManager.Shared.Dtos.Statements
{
    /// <summary>
    /// Request DTO for batch updating draft entries.
    /// </summary>
    public sealed class BatchUpdateRequestDto
    {
        /// <summary>List of entry updates to apply.</summary>
        /// <returns>The result.</returns>
        public List<EntryUpdateDto> Updates { get; set; } = new();

        /// <summary>Persisted entry identifiers to delete.</summary>
        /// <returns>The result.</returns>
        public List<Guid> Deletes { get; set; } = new();

        /// <summary>New entries to create during the same quick-edit save.</summary>
        /// <returns>The result.</returns>
        public List<EntryCreateDto> Creates { get; set; } = new();
    }

    /// <summary>Update for a single entry.</summary>
    public sealed class EntryUpdateDto
    {
        /// <summary>Entry identifier.</summary>
        public Guid EntryId { get; set; }
        /// <summary>Field key -> value mapping for changed fields.</summary>
        /// <returns>The result.</returns>
        public Dictionary<string, object?> Fields { get; set; } = new();
    }

    /// <summary>Create request for a local quick-edit entry.</summary>
    public sealed class EntryCreateDto
    {
        /// <summary>Client-side identifier used to map validation errors before persistence.</summary>
        public Guid ClientId { get; set; }
        /// <summary>Required booking date.</summary>
        public DateTime BookingDate { get; set; }
        /// <summary>Optional valuta date.</summary>
        public DateTime? ValutaDate { get; set; }
        /// <summary>Required amount. Must not be zero.</summary>
        public decimal Amount { get; set; }
        /// <summary>Required subject.</summary>
        public string Subject { get; set; } = string.Empty;
        /// <summary>Optional booking description.</summary>
        public string? BookingDescription { get; set; }
        /// <summary>Optional recipient name.</summary>
        public string? RecipientName { get; set; }
    }

    /// <summary>Success response for batch update.</summary>
    public sealed class BatchUpdateSuccessResponseDto
    {
        /// <summary>Success flag (true).</summary>
        public bool Success { get; set; } = true;
        /// <summary>Updated draft snapshot when update succeeded.</summary>
        public StatementDraftDetailDto? UpdatedDraft { get; set; }
    }

    /// <summary>Error response containing per-entry validation failures.</summary>
    public sealed class BatchUpdateErrorResponseDto
    {
        /// <summary>Success flag (false).</summary>
        public bool Success { get; set; } = false;
        /// <summary>Per-entry errors describing which fields failed validation.</summary>
        /// <returns>The result.</returns>
        public List<EntryErrorDto> Errors { get; set; } = new();
    }

    /// <summary>Errors for a single entry.</summary>
    public sealed class EntryErrorDto
    {
        /// <summary>Persisted entry identifier the errors belong to.</summary>
        public Guid? EntryId { get; set; }
        /// <summary>Client-side identifier for errors on new local rows.</summary>
        public Guid? ClientId { get; set; }
        /// <summary>Field-level errors for this entry.</summary>
        /// <returns>The result.</returns>
        public List<FieldErrorDto> FieldErrors { get; set; } = new();
    }

    /// <summary>Field-level validation error.</summary>
    public sealed class FieldErrorDto
    {
        /// <summary>Field key.</summary>
        public string Field { get; set; } = string.Empty;
        /// <summary>Human readable message (localized by caller/service).</summary>
        public string Message { get; set; } = string.Empty;
    }
}
