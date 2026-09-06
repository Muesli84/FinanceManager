// Application DTOs consolidated into shared contracts; keep using shared DTOs only
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using FinanceManager.Shared.Dtos.Statements;
using System.Globalization;
using System.Resources;
using System.Reflection;

namespace FinanceManager.Infrastructure.Statements;

public sealed partial class StatementDraftService
{
    private sealed class BatchEntryUpdateProposal
    {
        public DateTime BookingDate { get; set; }
        public DateTime? ValutaDate { get; set; }
        public decimal Amount { get; set; }
        public string Subject { get; set; } = string.Empty;
        public string? BookingDescription { get; set; }
        public string? RecipientName { get; set; }
        public string? CurrencyCode { get; set; }
        public StatementDraftEntryStatus? Status { get; set; }
    }

    /// <summary>
    /// Applies a batch of entry updates atomically. Validates inputs and applies all changes in a single DB transaction.
    /// Returns per-entry field errors when validation fails; in that case no changes are committed.
    /// </summary>
    /// <param name="draftId">The draft id.</param>
    /// <param name="ownerUserId">The owner user id.</param>
    /// <param name="request">Request payload.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <inheritdoc />
    /// <returns>The result.</returns>
    public async Task<(bool Success, FinanceManager.Shared.Dtos.Statements.BatchUpdateSuccessResponseDto SuccessResponse, FinanceManager.Shared.Dtos.Statements.BatchUpdateErrorResponseDto? ErrorResponse)> ApplyBatchEntryUpdatesAsync(Guid draftId, Guid ownerUserId, FinanceManager.Shared.Dtos.Statements.BatchUpdateRequestDto request, CancellationToken ct)
    {
        static string Loc(string key, string fallback)
        {
            try
            {
                var rm = new ResourceManager("FinanceManager.Infrastructure.Statements.Resources.StatementDraftService", Assembly.GetExecutingAssembly());
                var v = rm.GetString(key, CultureInfo.CurrentCulture);
                return string.IsNullOrEmpty(v) ? fallback : v;
            }
            catch
            {
                return fallback;
            }
        }

        if (request == null) throw new ArgumentNullException(nameof(request));

        _logger?.Log(LogLevel.Information, "User {User} requested batch update for draft {DraftId} with {UpdateCount} updates, {DeleteCount} deletes and {CreateCount} creates", ownerUserId, draftId, request.Updates?.Count ?? 0, request.Deletes?.Count ?? 0, request.Creates?.Count ?? 0);

        // Load draft with entries and check ownership
        var draft = await _db.StatementDrafts.Include(d => d.Entries).FirstOrDefaultAsync(d => d.Id == draftId, ct);
        if (draft == null || draft.OwnerUserId != ownerUserId)
        {
            _logger?.Log(LogLevel.Warning, "Unauthorized batch update attempt for draft {DraftId} by user {User}", draftId, ownerUserId);
            throw new UnauthorizedAccessException();
        }
        if (draft.Status != StatementDraftStatus.Draft)
        {
            // SuccessResponse is only meaningful when Success is true (see interface contract and the
            // controller, which only reads SuccessResponse after checking Success); null! here matches
            // that established convention since the tuple type does not support conditional nullability.
            return (false, null!, new FinanceManager.Shared.Dtos.Statements.BatchUpdateErrorResponseDto
            {
                Errors =
                {
                    new FinanceManager.Shared.Dtos.Statements.EntryErrorDto
                    {
                        FieldErrors = { new FinanceManager.Shared.Dtos.Statements.FieldErrorDto { Field = string.Empty, Message = "Draft is not editable" } }
                    }
                }
            });
        }

        var errors = new List<FinanceManager.Shared.Dtos.Statements.EntryErrorDto>();
        var updates = request.Updates ?? new List<FinanceManager.Shared.Dtos.Statements.EntryUpdateDto>();
        var deletes = request.Deletes ?? new List<Guid>();
        var creates = request.Creates ?? new List<FinanceManager.Shared.Dtos.Statements.EntryCreateDto>();

        // Map entries for quick lookup
        var entryMap = draft.Entries.ToDictionary(e => e.Id, e => e);

        var proposed = new Dictionary<Guid, BatchEntryUpdateProposal>();
        var updateIds = updates.Select(u => u.EntryId).ToHashSet();
        foreach (var deleteId in deletes.Distinct())
        {
            var entryErrors = new List<FinanceManager.Shared.Dtos.Statements.FieldErrorDto>();
            if (!entryMap.TryGetValue(deleteId, out var entry))
            {
                entryErrors.Add(new FinanceManager.Shared.Dtos.Statements.FieldErrorDto { Field = string.Empty, Message = "Entry not found in draft" });
            }
            else
            {
                if (updateIds.Contains(deleteId))
                {
                    entryErrors.Add(new FinanceManager.Shared.Dtos.Statements.FieldErrorDto { Field = string.Empty, Message = "Entry cannot be updated and deleted in the same request" });
                }
                if (entry.Status == StatementDraftEntryStatus.AlreadyBooked)
                {
                    entryErrors.Add(new FinanceManager.Shared.Dtos.Statements.FieldErrorDto { Field = string.Empty, Message = "Entry cannot be deleted in quick edit" });
                }
            }
            if (entryErrors.Count > 0)
            {
                errors.Add(new FinanceManager.Shared.Dtos.Statements.EntryErrorDto { EntryId = deleteId, FieldErrors = entryErrors });
            }
        }

        foreach (var upd in updates)
        {
            var entryErrors = new List<FinanceManager.Shared.Dtos.Statements.FieldErrorDto>();
            if (!entryMap.TryGetValue(upd.EntryId, out var entry))
            {
                entryErrors.Add(new FinanceManager.Shared.Dtos.Statements.FieldErrorDto { Field = string.Empty, Message = "Entry not found in draft" });
                errors.Add(new FinanceManager.Shared.Dtos.Statements.EntryErrorDto { EntryId = upd.EntryId, FieldErrors = entryErrors });
                continue;
            }

            // start from current values
            DateTime newBooking = entry.BookingDate;
            DateTime? newValuta = entry.ValutaDate;
            decimal newAmount = entry.Amount;
            string newSubject = entry.Subject;
            string? newBookingDesc = entry.BookingDescription;
            string? newRecipient = entry.RecipientName;
            string? newCurrency = entry.CurrencyCode;
            StatementDraftEntryStatus? newStatus = null;

            foreach (var kv in upd.Fields ?? new Dictionary<string, object?>())
            {
                var key = kv.Key ?? string.Empty;
                var val = kv.Value;
                switch (key)
                {
                    case "BookingDate":
                        if (val == null)
                        {
                            entryErrors.Add(new FinanceManager.Shared.Dtos.Statements.FieldErrorDto { Field = key, Message = Loc("BatchUpdate_BookingDateRequired", "Booking date is required") });
                            break;
                        }
                        DateTime parsedDate;
                        if (val is DateTime dtVal)
                        {
                            parsedDate = dtVal;
                        }
                        else
                        {
                            string? s = null;
                            if (val is System.Text.Json.JsonElement jeDate && jeDate.ValueKind == System.Text.Json.JsonValueKind.String)
                            {
                                s = jeDate.GetString();
                            }
                            else
                            {
                                s = Convert.ToString(val, CultureInfo.InvariantCulture);
                            }

                            // Accept only strict date formats (reject invalid calendar dates)
                            var formats = new[] { "yyyy-MM-dd", "dd.MM.yyyy", "yyyy/MM/dd" };
                            if (string.IsNullOrWhiteSpace(s) || !DateTime.TryParseExact(s.Trim(), formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsedDate))
                            {
                                entryErrors.Add(new FinanceManager.Shared.Dtos.Statements.FieldErrorDto { Field = key, Message = "Invalid date format" });
                                break;
                            }
                        }

                        // schedule change
                        newBooking = parsedDate.Date;
                        break;
                    case "ValutaDate":
                        if (val == null)
                        {
                            newValuta = null;
                            break;
                        }
                        DateTime parsedValuta;
                        if (val is DateTime dv)
                        {
                            parsedValuta = dv;
                        }
                        else
                        {
                            string? s2 = null;
                            if (val is System.Text.Json.JsonElement jeVal && jeVal.ValueKind == System.Text.Json.JsonValueKind.String)
                            {
                                s2 = jeVal.GetString();
                            }
                            else
                            {
                                s2 = Convert.ToString(val, CultureInfo.InvariantCulture);
                            }

                            var formats2 = new[] { "yyyy-MM-dd", "dd.MM.yyyy", "yyyy/MM/dd" };
                            if (string.IsNullOrWhiteSpace(s2) || !DateTime.TryParseExact(s2.Trim(), formats2, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsedValuta))
                            {
                                entryErrors.Add(new FinanceManager.Shared.Dtos.Statements.FieldErrorDto { Field = key, Message = "Invalid date format" });
                                break;
                            }
                        }
                        newValuta = parsedValuta.Date;
                        break;
                    case "Amount":
                        if (val == null)
                        {
                            entryErrors.Add(new FinanceManager.Shared.Dtos.Statements.FieldErrorDto { Field = key, Message = "Amount is required" });
                            break;
                        }
                        try
                        {
                            decimal dec;
                            if (val is System.Text.Json.JsonElement je)
                            {
                                if (je.ValueKind == System.Text.Json.JsonValueKind.Number)
                                {
                                    // try get decimal directly
                                    try { dec = je.GetDecimal(); }
                                    catch { dec = Convert.ToDecimal(je.GetDouble(), CultureInfo.InvariantCulture); }
                                }
                                else if (je.ValueKind == System.Text.Json.JsonValueKind.String)
                                {
                                    var se = je.GetString() ?? string.Empty;
                                    dec = Convert.ToDecimal(se, CultureInfo.InvariantCulture);
                                }
                                else
                                {
                                    entryErrors.Add(new FinanceManager.Shared.Dtos.Statements.FieldErrorDto { Field = key, Message = "Invalid amount" });
                                    break;
                                }
                            }
                            else
                            {
                                dec = Convert.ToDecimal(val, CultureInfo.InvariantCulture);
                            }

                            if (dec == 0m)
                            {
                                entryErrors.Add(new FinanceManager.Shared.Dtos.Statements.FieldErrorDto { Field = key, Message = "Amount must not be zero" });
                            }
                            else
                            {
                                newAmount = dec;
                            }
                        }
                        catch
                        {
                            entryErrors.Add(new FinanceManager.Shared.Dtos.Statements.FieldErrorDto { Field = key, Message = "Invalid amount" });
                        }
                        break;
                    case "Subject":
                        var subjText = Convert.ToString(val, CultureInfo.InvariantCulture) ?? string.Empty;
                        if (string.IsNullOrWhiteSpace(subjText))
                        {
                            entryErrors.Add(new FinanceManager.Shared.Dtos.Statements.FieldErrorDto { Field = key, Message = "Subject is required" });
                            break;
                        }
                        if (subjText.Length > 1000)
                        {
                            entryErrors.Add(new FinanceManager.Shared.Dtos.Statements.FieldErrorDto { Field = key, Message = "Subject too long" });
                        }
                        else
                        {
                            newSubject = subjText;
                        }
                        break;
                    case "BookingDescription":
                        var bd = Convert.ToString(val, CultureInfo.InvariantCulture) ?? string.Empty;
                        if (bd.Length > 1000)
                        {
                            entryErrors.Add(new FinanceManager.Shared.Dtos.Statements.FieldErrorDto { Field = key, Message = "Booking description too long" });
                        }
                        else
                        {
                            newBookingDesc = bd;
                        }
                        break;
                    case "RecipientName":
                        var r = Convert.ToString(val, CultureInfo.InvariantCulture) ?? string.Empty;
                        if (r.Length > 250)
                        {
                            entryErrors.Add(new FinanceManager.Shared.Dtos.Statements.FieldErrorDto { Field = key, Message = "Recipient name too long" });
                        }
                        else
                        {
                            newRecipient = string.IsNullOrWhiteSpace(r) ? null : r;
                        }
                        break;
                    case "Status":
                        if (val == null)
                        {
                            entryErrors.Add(new FinanceManager.Shared.Dtos.Statements.FieldErrorDto { Field = key, Message = "Status is required" });
                            break;
                        }
                        try
                        {
                            if (val is System.Text.Json.JsonElement jeStatus)
                            {
                                if (jeStatus.ValueKind == System.Text.Json.JsonValueKind.String)
                                {
                                    var sVal = jeStatus.GetString() ?? string.Empty;
                                    if (Enum.TryParse<StatementDraftEntryStatus>(sVal, true, out var st)) newStatus = st;
                                    else entryErrors.Add(new FinanceManager.Shared.Dtos.Statements.FieldErrorDto { Field = key, Message = "Invalid status value" });
                                }
                                else if (jeStatus.ValueKind == System.Text.Json.JsonValueKind.Number)
                                {
                                    var intVal = jeStatus.GetInt32();
                                    if (Enum.IsDefined(typeof(StatementDraftEntryStatus), intVal)) newStatus = (StatementDraftEntryStatus)intVal;
                                    else entryErrors.Add(new FinanceManager.Shared.Dtos.Statements.FieldErrorDto { Field = key, Message = "Invalid status value" });
                                }
                                else
                                {
                                    entryErrors.Add(new FinanceManager.Shared.Dtos.Statements.FieldErrorDto { Field = key, Message = "Invalid status value" });
                                }
                            }
                            else if (val is string sVal)
                            {
                                if (Enum.TryParse<StatementDraftEntryStatus>(sVal, true, out var st)) newStatus = st;
                                else entryErrors.Add(new FinanceManager.Shared.Dtos.Statements.FieldErrorDto { Field = key, Message = "Invalid status value" });
                            }
                            else
                            {
                                var intVal = Convert.ToInt32(val);
                                if (Enum.IsDefined(typeof(StatementDraftEntryStatus), intVal)) newStatus = (StatementDraftEntryStatus)intVal;
                                else entryErrors.Add(new FinanceManager.Shared.Dtos.Statements.FieldErrorDto { Field = key, Message = "Invalid status value" });
                            }
                        }
                        catch
                        {
                            entryErrors.Add(new FinanceManager.Shared.Dtos.Statements.FieldErrorDto { Field = key, Message = "Invalid status value" });
                        }
                        break;
                    default:
                        // ignore unknown fields per API guidance
                        _logger?.Log(LogLevel.Debug, "Ignoring unknown field '{Field}' in batch update for entry {EntryId}", key, upd.EntryId);
                        break;
                }
            }

            var nonStatusFields = upd.Fields?.Keys.Where(k => !string.Equals(k, "Status", StringComparison.Ordinal)).ToList() ?? new List<string>();
            if (entry.Status == StatementDraftEntryStatus.AlreadyBooked
                && nonStatusFields.Count > 0
                && newStatus != StatementDraftEntryStatus.Open)
            {
                entryErrors.Add(new FinanceManager.Shared.Dtos.Statements.FieldErrorDto { Field = string.Empty, Message = "Entry is not editable" });
            }

            if (entryErrors.Count > 0)
            {
                errors.Add(new FinanceManager.Shared.Dtos.Statements.EntryErrorDto { EntryId = upd.EntryId, FieldErrors = entryErrors });
            }
            else
            {
                proposed[upd.EntryId] = new BatchEntryUpdateProposal
                {
                    BookingDate = newBooking,
                    ValutaDate = newValuta,
                    Amount = newAmount,
                    Subject = newSubject,
                    BookingDescription = newBookingDesc,
                    RecipientName = newRecipient,
                    CurrencyCode = newCurrency,
                    Status = newStatus
                };
            }
        }

        foreach (var create in creates)
        {
            var entryErrors = new List<FinanceManager.Shared.Dtos.Statements.FieldErrorDto>();
            if (create.ClientId == Guid.Empty)
            {
                entryErrors.Add(new FinanceManager.Shared.Dtos.Statements.FieldErrorDto { Field = nameof(create.ClientId), Message = "Client id is required" });
            }
            if (create.BookingDate == DateTime.MinValue)
            {
                entryErrors.Add(new FinanceManager.Shared.Dtos.Statements.FieldErrorDto { Field = nameof(create.BookingDate), Message = "Booking date is required" });
            }
            if (create.Amount == 0m)
            {
                entryErrors.Add(new FinanceManager.Shared.Dtos.Statements.FieldErrorDto { Field = nameof(create.Amount), Message = "Amount must not be zero" });
            }
            if (string.IsNullOrWhiteSpace(create.Subject))
            {
                entryErrors.Add(new FinanceManager.Shared.Dtos.Statements.FieldErrorDto { Field = nameof(create.Subject), Message = "Subject is required" });
            }
            else if (create.Subject.Length > 1000)
            {
                entryErrors.Add(new FinanceManager.Shared.Dtos.Statements.FieldErrorDto { Field = nameof(create.Subject), Message = "Subject too long" });
            }
            if ((create.BookingDescription?.Length ?? 0) > 1000)
            {
                entryErrors.Add(new FinanceManager.Shared.Dtos.Statements.FieldErrorDto { Field = nameof(create.BookingDescription), Message = "Booking description too long" });
            }
            if ((create.RecipientName?.Length ?? 0) > 250)
            {
                entryErrors.Add(new FinanceManager.Shared.Dtos.Statements.FieldErrorDto { Field = nameof(create.RecipientName), Message = "Recipient name too long" });
            }
            if (entryErrors.Count > 0)
            {
                errors.Add(new FinanceManager.Shared.Dtos.Statements.EntryErrorDto { ClientId = create.ClientId, FieldErrors = entryErrors });
            }
        }

        if (errors.Count > 0)
        {
            var errResp = new FinanceManager.Shared.Dtos.Statements.BatchUpdateErrorResponseDto { Errors = errors };
            return (false, null!, errResp);
        }

        // All validations passed -> apply changes in transaction
        using var tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            var splitDraftIdsToReevaluate = new HashSet<Guid>();
            foreach (var deleteId in deletes.Distinct())
            {
                if (!entryMap.TryGetValue(deleteId, out var entry))
                {
                    continue;
                }
                if (entry.SplitDraftId.HasValue)
                {
                    splitDraftIdsToReevaluate.Add(entry.SplitDraftId.Value);
                }
                _db.StatementDraftEntries.Remove(entry);
            }

            foreach (var kv in proposed)
            {
                var entryId = kv.Key;
                var p = kv.Value;
                if (!entryMap.TryGetValue(entryId, out var ent))
                {
                    continue;
                }

                ent.UpdateCore(p.BookingDate, p.ValutaDate, p.Amount, p.Subject, p.RecipientName, p.CurrencyCode, p.BookingDescription);

                // If caller requested an explicit status change (e.g., mark as AlreadyBooked), apply it here.
                if (p.Status.HasValue)
                {
                    if (p.Status.Value == StatementDraftEntryStatus.AlreadyBooked)
                    {
                        ent.MarkAlreadyBooked();
                    }
                    else if (p.Status.Value == StatementDraftEntryStatus.Accounted)
                    {
                        if (ent.ContactId.HasValue)
                            ent.MarkAccounted(ent.ContactId.Value);
                        else
                            ent.MarkNeedsCheck();
                    }
                    else if (p.Status.Value == StatementDraftEntryStatus.Announced)
                    {
                        ent.ResetOpen();
                    }
                    else if (p.Status.Value == StatementDraftEntryStatus.Open)
                    {
                        ent.MarkNeedsCheck();
                    }
                }
                if (ent.SplitDraftId.HasValue)
                {
                    splitDraftIdsToReevaluate.Add(ent.SplitDraftId.Value);
                }
            }

            var createdEntryIds = new List<Guid>();
            foreach (var create in creates)
            {
                var entry = draft.AddEntry(
                    create.BookingDate.Date,
                    create.Amount,
                    create.Subject.Trim(),
                    string.IsNullOrWhiteSpace(create.RecipientName) ? null : create.RecipientName.Trim(),
                    create.ValutaDate?.Date,
                    null,
                    string.IsNullOrWhiteSpace(create.BookingDescription) ? null : create.BookingDescription.Trim(),
                    false,
                    false);
                _db.Entry(entry).State = EntityState.Added;
                createdEntryIds.Add(entry.Id);
            }

            await _db.SaveChangesAsync(ct);

            foreach (var createdEntryId in createdEntryIds)
            {
                await ClassifyInternalAsync(draft, createdEntryId, ownerUserId, ct);
            }
            await _db.SaveChangesAsync(ct);

            if (await _db.StatementDraftEntries.AnyAsync(e => e.SplitDraftId == draft.Id, ct))
            {
                await ReevaluateParentEntryStatusAsync(ownerUserId, draft.Id, ct);
            }
            foreach (var splitDraftId in splitDraftIdsToReevaluate)
            {
                await ReevaluateParentEntryStatusAsync(ownerUserId, splitDraftId, ct);
            }
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            // return updated draft snapshot
            var updated = await GetDraftAsync(draftId, ownerUserId, ct);
            // Map to StatementDraftDetailDto expected by shared contract
            FinanceManager.Shared.Dtos.Statements.StatementDraftDetailDto? detail = null;
            if (updated != null)
            {
                // Map available fields from StatementDraftDto to the detailed DTO. Prev/Next and extra maps are not available here and kept null.
                detail = new FinanceManager.Shared.Dtos.Statements.StatementDraftDetailDto(
                    updated.DraftId,
                    updated.OriginalFileName ?? string.Empty,
                    updated.Description,
                    updated.DetectedAccountId,
                    updated.Status,
                    updated.TotalAmount,
                    updated.IsSplitDraft,
                    updated.ParentDraftId,
                    updated.ParentEntryId,
                    updated.ParentEntryAmount,
                    updated.UploadGroupId,
                    updated.Entries ?? new List<FinanceManager.Shared.Dtos.Statements.StatementDraftEntryDto>(),
                    PrevInUpload: null,
                    NextInUpload: null,
                    IsPreliminary: updated.IsPreliminary
                );
            }
            var success = new FinanceManager.Shared.Dtos.Statements.BatchUpdateSuccessResponseDto { UpdatedDraft = detail };
            _logger?.Log(LogLevel.Information, "Batch quick-edit save applied for draft {DraftId} by user {User}", draftId, ownerUserId);
            return (true, success, null);
        }
        catch (Exception ex)
        {
            _logger?.Log(LogLevel.Error, ex, "Failed to commit batch update for draft {DraftId}", draftId);
            await tx.RollbackAsync(ct);
            throw;
        }
    }
}
