using System.Net.Http.Json;
using System.Text.Json;

namespace FinanceManager.Shared;

public partial class ApiClient
{
    #region Statement Drafts

    /// <summary>
    /// Lists open statement drafts for the current user with optional paging.
    /// </summary>
    /// <param name="skip">Number of items to skip for paging.</param>
    /// <param name="take">Number of items to take for paging.</param>
    /// <param name="ct">Cancellation token to cancel the HTTP request.</param>
    /// <returns>A read-only list of <see cref="StatementDraftDto"/>. Returns an empty list when none are available.</returns>
    /// <exception cref="HttpRequestException">When the HTTP request fails or the server returns a non-success status code.</exception>
    public async Task<IReadOnlyList<StatementDraftDto>> StatementDrafts_ListOpenAsync(int skip = 0, int take = 3, CancellationToken ct = default)
    {
        var resp = await _http.GetAsync($"/api/statement-drafts?skip={skip}&take={take}", ct);
        await EnsureSuccessOrSetErrorAsync(resp);
        return await resp.Content.ReadFromJsonAsync<IReadOnlyList<StatementDraftDto>>(cancellationToken: ct) ?? Array.Empty<StatementDraftDto>();
    }

    /// <summary>
    /// Gets the count of open statement drafts for the current user.
    /// </summary>
    /// <param name="ct">Cancellation token to cancel the HTTP request.</param>
    /// <returns>Number of open statement drafts.</returns>
    /// <exception cref="HttpRequestException">When the HTTP request fails or the server returns a non-success status code.</exception>
    public async Task<int> StatementDrafts_GetOpenCountAsync(CancellationToken ct = default)
    {
        var resp = await _http.GetAsync("/api/statement-drafts/count", ct);
        await EnsureSuccessOrSetErrorAsync(resp);
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        if (json.TryGetProperty("count", out var prop) && prop.TryGetInt32(out var cnt)) return cnt;
        return 0;
    }

    /// <summary>
    /// Deletes all statement drafts for the current user. Caution: irreversible.
    /// </summary>
    /// <param name="ct">Cancellation token to cancel the HTTP request.</param>
    /// <returns>True when the operation was accepted by the server.</returns>
    /// <exception cref="HttpRequestException">When the HTTP request fails.</exception>
    public async Task<bool> StatementDrafts_DeleteAllAsync(CancellationToken ct = default)
    {
        var resp = await _http.DeleteAsync("/api/statement-drafts/all", ct);
        resp.EnsureSuccessStatusCode();
        return true;
    }

    /// <summary>
    /// Uploads a statement file for processing and returns details about created drafts.
    /// </summary>
    /// <param name="stream">Stream containing the statement file.</param>
    /// <param name="fileName">Filename to send to the server.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Upload result information or null on failure.</returns>
    /// <exception cref="ArgumentNullException">When <paramref name="stream"/> or <paramref name="fileName"/> is null.</exception>
    public async Task<StatementDraftUploadResult?> StatementDrafts_UploadAsync(Stream stream, string fileName, CancellationToken ct = default)
    {
        using var content = new MultipartFormDataContent();
        content.Add(new StreamContent(stream), "file", fileName);
        var resp = await _http.PostAsync("/api/statement-drafts/upload", content, ct);
        if (!resp.IsSuccessStatusCode) return null;
        var primary = await resp.Content.ReadFromJsonAsync<StatementDraftUploadResult>(cancellationToken: ct);
        if (primary != null) return primary;
        var wrapper = await resp.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        if (wrapper.ValueKind == JsonValueKind.Object)
        {
            if (wrapper.TryGetProperty("Result", out var r) && r.ValueKind == JsonValueKind.Object)
            {
                var result = r.Deserialize<StatementDraftUploadResult>();
                if (result != null) return result;
            }
            if (wrapper.TryGetProperty("Legacy", out var l) && l.ValueKind == JsonValueKind.Object)
            {
                var legacy = l.Deserialize<StatementDraftUploadResult>();
                if (legacy != null) return legacy;
            }
            if (wrapper.TryGetProperty("FirstDraft", out var fd) && fd.ValueKind == JsonValueKind.Object)
            {
                var first = fd.Deserialize<StatementDraftDto>();
                return new StatementDraftUploadResult(first, null);
            }
        }
        return null;
    }

    /// <summary>
    /// Analyzes or executes a mixed start-page mass import batch.
    /// </summary>
    /// <param name="request">Mass import batch request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Batch result or null when the request failed.</returns>
    public async Task<MassImportBatchResultDto?> StatementDrafts_ProcessMassImportAsync(MassImportBatchRequestDto request, CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync("/api/statement-drafts/mass-import", request, ct);
        if (!resp.IsSuccessStatusCode)
        {
            await EnsureSuccessOrSetErrorAsync(resp);
            return null;
        }

        return await resp.Content.ReadFromJsonAsync<MassImportBatchResultDto>(cancellationToken: ct);
    }

    /// <summary>
    /// Creates an empty statement draft (no file) for the current user.
    /// </summary>
    /// <param name="fileName">Optional name for the draft's original file.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Detail DTO of the created draft or null on failure.</returns>
    /// <exception cref="HttpRequestException">When the HTTP request fails.</exception>
    public async Task<StatementDraftDetailDto?> StatementDrafts_CreateAsync(string? fileName = null, CancellationToken ct = default)
    {
        var url = "/api/statement-drafts";
        if (!string.IsNullOrWhiteSpace(fileName)) url += $"?fileName={Uri.EscapeDataString(fileName)}";
        var resp = await _http.PostAsync(url, content: null, ct);
        if (!resp.IsSuccessStatusCode)
        {
            await EnsureSuccessOrSetErrorAsync(resp);
            return null;
        }
        return await resp.Content.ReadFromJsonAsync<StatementDraftDetailDto>(cancellationToken: ct);
    }

    /// <summary>
    /// Gets the detailed information for a statement draft.
    /// </summary>
    /// <param name="draftId">Draft identifier.</param>
    /// <param name="headerOnly">When true, returns header-only information.</param>
    /// <param name="src">Optional source identifier to influence returned view.</param>
    /// <param name="fromEntryDraftId">Optional id to prefill data from another draft entry.</param>
    /// <param name="fromEntryId">Optional id to prefill data from an existing entry.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Draft detail DTO or null when not found.</returns>
    /// <exception cref="HttpRequestException">When the HTTP request fails.</exception>
    public async Task<StatementDraftDetailDto?> StatementDrafts_GetAsync(Guid draftId, bool headerOnly = false, string? src = null, Guid? fromEntryDraftId = null, Guid? fromEntryId = null, CancellationToken ct = default)
    {
        var url = $"/api/statement-drafts/{draftId}?headerOnly={(headerOnly ? "true" : "false")}";
        if (!string.IsNullOrWhiteSpace(src)) url += $"&src={Uri.EscapeDataString(src)}";
        if (fromEntryDraftId.HasValue) url += $"&fromEntryDraftId={fromEntryDraftId.Value}";
        if (fromEntryId.HasValue) url += $"&fromEntryId={fromEntryId.Value}";
        var resp = await _http.GetAsync(url, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        await EnsureSuccessOrSetErrorAsync(resp);
        return await resp.Content.ReadFromJsonAsync<StatementDraftDetailDto>(cancellationToken: ct);
    }

    /// <summary>
    /// Gets details for a specific entry inside a statement draft.
    /// </summary>
    /// <param name="draftId">Draft identifier.</param>
    /// <param name="entryId">Entry identifier inside the draft.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Entry detail DTO or null when not found.</returns>
    public async Task<StatementDraftEntryDetailDto?> StatementDrafts_GetEntryAsync(Guid draftId, Guid entryId, CancellationToken ct = default)
    {
        var resp = await _http.GetAsync($"/api/statement-drafts/{draftId}/entries/{entryId}", ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        await EnsureSuccessOrSetErrorAsync(resp);
        return await resp.Content.ReadFromJsonAsync<StatementDraftEntryDetailDto>(cancellationToken: ct);
    }

    /// <summary>
    /// Calls the backend batch update endpoint for statement draft entries.
    /// Returns the success DTO on 200 or structured per-entry errors on 400.
    /// Uses explicit serialization to avoid empty-body Json deserialization errors.
    /// </summary>
    /// <param name="draftId">The draft id.</param>
    /// <param name="req">Request payload.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    public async Task<(FinanceManager.Shared.Dtos.Statements.BatchUpdateSuccessResponseDto? Success, FinanceManager.Shared.Dtos.Statements.BatchUpdateErrorResponseDto? Error)> StatementDrafts_BatchUpdateDetailedAsync(Guid draftId, FinanceManager.Shared.Dtos.Statements.BatchUpdateRequestDto req, CancellationToken ct = default)
    {
        // Some servers expect date fields in a specific string format. To avoid
        // "Invalid date format" errors for boxed DateTime values inside the
        // generic Fields dictionary, convert DateTime/DateTimeOffset values to
        // a stable date-only string (ISO yyyy-MM-dd) before serialization.
        static string FormatDate(DateTime value)
            => value.Date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

        var sanitizedUpdates = new System.Collections.Generic.List<FinanceManager.Shared.Dtos.Statements.EntryUpdateDto>();
        if (req?.Updates != null)
        {
            foreach (var u in req.Updates)
            {
                var nu = new FinanceManager.Shared.Dtos.Statements.EntryUpdateDto { EntryId = u.EntryId, Fields = new System.Collections.Generic.Dictionary<string, object?>(StringComparer.Ordinal) };
                if (u.Fields != null)
                {
                    foreach (var kv in u.Fields)
                    {
                        if (kv.Value is DateTime dt)
                        {
                            nu.Fields[kv.Key] = FormatDate(dt);
                        }
                        else if (kv.Value is DateTimeOffset dto)
                        {
                            nu.Fields[kv.Key] = dto.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
                        }
                        else
                        {
                            nu.Fields[kv.Key] = kv.Value;
                        }
                    }
                }
                sanitizedUpdates.Add(nu);
            }
        }

        var creates = req?.Creates ?? new System.Collections.Generic.List<FinanceManager.Shared.Dtos.Statements.EntryCreateDto>();
        var payload = new
        {
            Updates = sanitizedUpdates,
            Deletes = req?.Deletes != null ? new System.Collections.Generic.List<Guid>(req.Deletes) : new System.Collections.Generic.List<Guid>(),
            Creates = creates.Select(c => new
            {
                c.ClientId,
                BookingDate = FormatDate(c.BookingDate),
                ValutaDate = c.ValutaDate.HasValue ? FormatDate(c.ValutaDate.Value) : null,
                c.Amount,
                c.Subject,
                c.BookingDescription,
                c.RecipientName
            }).ToList()
        };

        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        using var content = new StringContent(JsonSerializer.Serialize(payload, options), System.Text.Encoding.UTF8, "application/json");
        var resp = await _http.PostAsync($"/api/statement-drafts/{draftId}/entries/batch-update", content, ct);
        if (resp.IsSuccessStatusCode)
        {
            try
            {
                var ok = await resp.Content.ReadFromJsonAsync<FinanceManager.Shared.Dtos.Statements.BatchUpdateSuccessResponseDto>(cancellationToken: ct);
                return (ok, null);
            }
            catch
            {
                // successful but no body -> return a success marker
                return (new FinanceManager.Shared.Dtos.Statements.BatchUpdateSuccessResponseDto { UpdatedDraft = null }, null);
            }
        }

        // Try parse structured error response (per-entry errors)
        try
        {
            var err = await resp.Content.ReadFromJsonAsync<FinanceManager.Shared.Dtos.Statements.BatchUpdateErrorResponseDto>(cancellationToken: ct);
            if (err != null)
            {
                return (null, err);
            }
        }
        catch
        {

            // ignore parse errors
        }

        try
        {
            await EnsureSuccessOrSetErrorAsync(resp);
        }
        catch
        {
            // EnsureSuccessOrSetErrorAsync populated LastError/LastErrorCode
        }
        return (null, null);
    }

    /// <summary>
    /// Updates core fields of a draft entry (dates, amount, textual fields).
    /// </summary>
    /// <param name="draftId">Draft identifier.</param>
    /// <param name="entryId">Entry identifier.</param>
    /// <param name="req">Update request containing new core values.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Updated entry DTO or null when not found.</returns>
    public async Task<StatementDraftEntryDto?> StatementDrafts_UpdateEntryCoreAsync(Guid draftId, Guid entryId, StatementDraftUpdateEntryCoreRequest req, CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync($"/api/statement-drafts/{draftId}/entries/{entryId}/edit-core", req, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<StatementDraftEntryDto>(cancellationToken: ct);
    }

    /// <summary>
    /// Adds a new entry to an existing statement draft.
    /// </summary>
    /// <param name="draftId">Draft identifier.</param>
    /// <param name="req">Request describing the new entry.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Updated draft detail DTO or null when draft not found.</returns>
    public async Task<StatementDraftDetailDto?> StatementDrafts_AddEntryAsync(Guid draftId, StatementDraftAddEntryRequest req, CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync($"/api/statement-drafts/{draftId}/entries", req, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        await EnsureSuccessOrSetErrorAsync(resp);
        return await resp.Content.ReadFromJsonAsync<StatementDraftDetailDto>(cancellationToken: ct);
    }

    /// <summary>
    /// Triggers automatic classification for a draft (server-side).
    /// </summary>
    /// <param name="draftId">Draft identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Updated draft detail DTO or null when request is invalid or draft not found.</returns>
    public async Task<StatementDraftDetailDto?> StatementDrafts_ClassifyAsync(Guid draftId, CancellationToken ct = default)
    {
        var resp = await _http.PostAsync($"/api/statement-drafts/{draftId}/classify", content: null, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.BadRequest) return null;
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        await EnsureSuccessOrSetErrorAsync(resp);
        return await resp.Content.ReadFromJsonAsync<StatementDraftDetailDto>(cancellationToken: ct);
    }

    /// <summary>
    /// Sets the account associated with a statement draft.
    /// </summary>
    /// <param name="draftId">Draft identifier.</param>
    /// <param name="accountId">Account id to assign.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Updated draft detail DTO or null when not found.</returns>
    public async Task<StatementDraftDetailDto?> StatementDrafts_SetAccountAsync(Guid draftId, Guid accountId, CancellationToken ct = default)
    {
        var resp = await _http.PostAsync($"/api/statement-drafts/{draftId}/account/{accountId}", content: null, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        await EnsureSuccessOrSetErrorAsync(resp);
        return await resp.Content.ReadFromJsonAsync<StatementDraftDetailDto>(cancellationToken: ct);
    }

    /// <summary>
    /// Sets the description for a draft.
    /// </summary>
    /// <param name="draftId">Draft identifier.</param>
    /// <param name="description">New description or null to clear.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Updated draft detail DTO or null when not found.</returns>
    public async Task<StatementDraftDetailDto?> StatementDrafts_SetDescriptionAsync(Guid draftId, string? description, CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync($"/api/statement-drafts/{draftId}/description", description, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        await EnsureSuccessOrSetErrorAsync(resp);
        return await resp.Content.ReadFromJsonAsync<StatementDraftDetailDto>(cancellationToken: ct);
    }

    /// <summary>
    /// Creates a new preliminary (provisional) statement draft for the specified bank account.
    /// </summary>
    /// <param name="request">Request containing the account identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Created draft DTO or null when not found.</returns>
    public async Task<StatementDraftDto?> StatementDrafts_CreatePreliminaryAsync(CreatePreliminaryStatementDraftRequest request, CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync("/api/statement-drafts/preliminary", request, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        await EnsureSuccessOrSetErrorAsync(resp);
        return await resp.Content.ReadFromJsonAsync<StatementDraftDto>(cancellationToken: ct);
    }

    /// <summary>
    /// Commits (books) a statement draft. This finalizes the draft and creates postings.
    /// </summary>
    /// <param name="draftId">Draft identifier.</param>
    /// <param name="req">Commit options.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Result object from the server (format may vary) or null when draft not found.</returns>
    public async Task<object?> StatementDrafts_CommitAsync(Guid draftId, StatementDraftCommitRequest req, CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync($"/api/statement-drafts/{draftId}/commit", req, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        return json;
    }

    /// <summary>
    /// Assigns a contact to a draft entry.
    /// </summary>
    /// <param name="draftId">Draft identifier.</param>
    /// <param name="entryId">Entry identifier.</param>
    /// <param name="req">Contact assignment request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Updated entry DTO or null when not found.</returns>
    public async Task<StatementDraftEntryDto?> StatementDrafts_SetEntryContactAsync(Guid draftId, Guid entryId, StatementDraftSetContactRequest req, CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync($"/api/statement-drafts/{draftId}/entries/{entryId}/contact", req, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<StatementDraftEntryDto>(cancellationToken: ct);
    }

    /// <summary>
    /// Sets or clears the cost-neutral flag for an entry.
    /// </summary>
    /// <param name="draftId">Draft identifier.</param>
    /// <param name="entryId">Entry identifier.</param>
    /// <param name="req">Request indicating cost-neutral flag state.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Updated entry DTO or null when not found.</returns>
    public async Task<StatementDraftEntryDto?> StatementDrafts_SetEntryCostNeutralAsync(Guid draftId, Guid entryId, StatementDraftSetCostNeutralRequest req, CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync($"/api/statement-drafts/{draftId}/entries/{entryId}/costneutral", req, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<StatementDraftEntryDto>(cancellationToken: ct);
    }

    /// <summary>
    /// Sets the savings plan for an entry.
    /// </summary>
    /// <param name="draftId">Draft identifier.</param>
    /// <param name="entryId">Entry identifier.</param>
    /// <param name="req">Savings plan assignment request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Updated entry DTO or null when not found.</returns>
    public async Task<StatementDraftEntryDto?> StatementDrafts_SetEntrySavingsPlanAsync(Guid draftId, Guid entryId, StatementDraftSetSavingsPlanRequest req, CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync($"/api/statement-drafts/{draftId}/entries/{entryId}/savingsplan", req, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<StatementDraftEntryDto>(cancellationToken: ct);
    }

    /// <summary>
    /// Sets the security properties for an entry (e.g. link to a security).
    /// </summary>
    /// <param name="draftId">Draft identifier.</param>
    /// <param name="entryId">Entry identifier.</param>
    /// <param name="req">Security assignment request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Updated entry DTO or null when not found.</returns>
    public async Task<StatementDraftEntryDto?> StatementDrafts_SetEntrySecurityAsync(Guid draftId, Guid entryId, StatementDraftSetEntrySecurityRequest req, CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync($"/api/statement-drafts/{draftId}/entries/{entryId}/security", req, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<StatementDraftEntryDto>(cancellationToken: ct);
    }

    /// <summary>
    /// Sets the archive-on-booking flag for a savings-plan entry.
    /// </summary>
    /// <param name="draftId">Draft identifier.</param>
    /// <param name="entryId">Entry identifier.</param>
    /// <param name="req">Archive-on-booking request.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Updated entry DTO or null when not found.</returns>
    public async Task<StatementDraftEntryDto?> StatementDrafts_SetEntryArchiveOnBookingAsync(Guid draftId, Guid entryId, StatementDraftSetArchiveSavingsPlanOnBookingRequest req, CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync($"/api/statement-drafts/{draftId}/entries/{entryId}/savingsplan/archive-on-booking", req, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<StatementDraftEntryDto>(cancellationToken: ct);
    }

    /// <summary>
    /// Validates the statement draft and returns any validation messages.
    /// </summary>
    /// <param name="draftId">Draft identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Validation result DTO or null on error.</returns>
    public async Task<DraftValidationResultDto?> StatementDrafts_ValidateAsync(Guid draftId, CancellationToken ct = default)
    {
        var resp = await _http.GetAsync($"/api/statement-drafts/{draftId}/validate", ct);
        await EnsureSuccessOrSetErrorAsync(resp);
        return await resp.Content.ReadFromJsonAsync<DraftValidationResultDto>(cancellationToken: ct);
    }

    /// <summary>
    /// Validates a specific entry in a draft.
    /// </summary>
    /// <param name="draftId">Draft identifier.</param>
    /// <param name="entryId">Entry identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Validation result DTO or null on error.</returns>
    public async Task<DraftValidationResultDto?> StatementDrafts_ValidateEntryAsync(Guid draftId, Guid entryId, CancellationToken ct = default)
    {
        var resp = await _http.GetAsync($"/api/statement-drafts/{draftId}/entries/{entryId}/validate", ct);
        await EnsureSuccessOrSetErrorAsync(resp);
        return await resp.Content.ReadFromJsonAsync<DraftValidationResultDto>(cancellationToken: ct);
    }

    /// <summary>
    /// Books (commits) a draft and returns booking result including warnings or errors.
    /// </summary>
    /// <param name="draftId">Draft identifier.</param>
    /// <param name="forceWarnings">When true, proceeds despite warnings.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Booking result DTO or null when server refused the operation.</returns>
    public async Task<BookingResult?> StatementDrafts_BookAsync(Guid draftId, bool forceWarnings = false, CancellationToken ct = default)
    {
        var resp = await _http.PostAsync($"/api/statement-drafts/{draftId}/book?forceWarnings={(forceWarnings ? "true" : "false")}", content: null, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.BadRequest)
        {
            // validation errors
            return await resp.Content.ReadFromJsonAsync<BookingResult>(cancellationToken: ct);
        }
        if (resp.StatusCode == System.Net.HttpStatusCode.PreconditionRequired)
        {
            return await resp.Content.ReadFromJsonAsync<BookingResult>(cancellationToken: ct);
        }
        await EnsureSuccessOrSetErrorAsync(resp);
        return await resp.Content.ReadFromJsonAsync<BookingResult>(cancellationToken: ct);
    }

    /// <summary>
    /// Books a single entry within a draft and returns the booking result.
    /// </summary>
    /// <param name="draftId">Draft identifier.</param>
    /// <param name="entryId">Entry identifier.</param>
    /// <param name="forceWarnings">Whether to force booking despite warnings.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Booking result DTO or null when operation not applicable.</returns>
    public async Task<BookingResult?> StatementDrafts_BookEntryAsync(Guid draftId, Guid entryId, bool forceWarnings = false, CancellationToken ct = default)
    {
        var resp = await _http.PostAsync($"/api/statement-drafts/{draftId}/entries/{entryId}/book?forceWarnings={(forceWarnings ? "true" : "false")}", content: null, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.BadRequest)
        {
            return await resp.Content.ReadFromJsonAsync<BookingResult>(cancellationToken: ct);
        }
        if (resp.StatusCode == System.Net.HttpStatusCode.PreconditionRequired)
        {
            return await resp.Content.ReadFromJsonAsync<BookingResult>(cancellationToken: ct);
        }
        await EnsureSuccessOrSetErrorAsync(resp);
        return await resp.Content.ReadFromJsonAsync<BookingResult>(cancellationToken: ct);
    }

    /// <summary>
    /// Saves all details of an entry in a statement draft.
    /// </summary>
    /// <param name="draftId">Draft identifier.</param>
    /// <param name="entryId">Entry identifier.</param>
    /// <param name="req">Request containing all fields to save.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Server response object or null when not found.</returns>
    public async Task<object?> StatementDrafts_SaveEntryAllAsync(Guid draftId, Guid entryId, StatementDraftSaveEntryAllRequest req, CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync($"/api/statement-drafts/{draftId}/entries/{entryId}/save-all", req, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        await EnsureSuccessOrSetErrorAsync(resp);
        return await resp.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
    }

    /// <summary>
    /// Deletes an entry from a statement draft.
    /// </summary>
    /// <param name="draftId">Draft identifier.</param>
    /// <param name="entryId">Entry identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True when deleted; false when not found.</returns>
    public async Task<bool> StatementDrafts_DeleteEntryAsync(Guid draftId, Guid entryId, CancellationToken ct = default)
    {
        var resp = await _http.DeleteAsync($"/api/statement-drafts/{draftId}/entries/{entryId}", ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return false;
        await EnsureSuccessOrSetErrorAsync(resp);
        return true;
    }

    /// <summary>
    /// Resets a duplicate marker on an entry and returns server response.
    /// </summary>
    /// <param name="draftId">Draft identifier.</param>
    /// <param name="entryId">Entry identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Server response object or null when not found.</returns>
    public async Task<object?> StatementDrafts_ResetDuplicateEntryAsync(Guid draftId, Guid entryId, CancellationToken ct = default)
    {
        var resp = await _http.PostAsync($"/api/statement-drafts/{draftId}/entries/{entryId}/reset-duplicate", content: null, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        await EnsureSuccessOrSetErrorAsync(resp);
        return await resp.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
    }

    /// <summary>
    /// Triggers classification of a single entry in a draft.
    /// </summary>
    /// <param name="draftId">Draft identifier.</param>
    /// <param name="entryId">Entry identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Updated draft detail DTO or null when not found or request invalid.</returns>
    public async Task<StatementDraftDetailDto?> StatementDrafts_ClassifyEntryAsync(Guid draftId, Guid entryId, CancellationToken ct = default)
    {
        var resp = await _http.PostAsync($"/api/statement-drafts/{draftId}/entries/{entryId}/classify-entry", content: null, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.BadRequest) return null;
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        await EnsureSuccessOrSetErrorAsync(resp);
        return await resp.Content.ReadFromJsonAsync<StatementDraftDetailDto>(cancellationToken: ct);
    }

    /// <summary>
    /// Downloads the original file uploaded for the draft.
    /// </summary>
    /// <param name="draftId">Draft identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Stream containing the original file or null when not found.</returns>
    public async Task<Stream?> StatementDrafts_DownloadOriginalAsync(Guid draftId, CancellationToken ct = default)
    {
        var resp = await _http.GetAsync($"/api/statement-drafts/{draftId}/file", ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        await EnsureSuccessOrSetErrorAsync(resp);
        var ms = new MemoryStream();
        await resp.Content.CopyToAsync(ms, ct);
        ms.Position = 0;
        return ms;
    }

    /// <summary>
    /// Deletes a statement draft by id.
    /// </summary>
    /// <param name="draftId">Draft identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True when deleted; false when not found.</returns>
    public async Task<bool> StatementDrafts_DeleteAsync(Guid draftId, CancellationToken ct = default)
    {
        var resp = await _http.DeleteAsync($"/api/statement-drafts/{draftId}", ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return false;
        await EnsureSuccessOrSetErrorAsync(resp);
        return true;
    }

    /// <summary>
    /// Assigns or clears a split-draft group for a draft entry and returns updated split difference.
    /// </summary>
    /// <param name="draftId">Draft identifier.</param>
    /// <param name="entryId">Entry identifier.</param>
    /// <param name="req">Split assignment request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Result DTO with updated split information or null when not found/invalid.</returns>
    public async Task<StatementDraftSetEntrySplitDraftResultDto?> StatementDrafts_SetEntrySplitDraftAsync(Guid draftId, Guid entryId, StatementDraftSetSplitDraftRequest req, CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync($"/api/statement-drafts/{draftId}/entries/{entryId}/split", req, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        if (resp.StatusCode == System.Net.HttpStatusCode.BadRequest)
        {
            LastError = await resp.Content.ReadAsStringAsync(ct);
            return null;
        }
        await EnsureSuccessOrSetErrorAsync(resp);
        var result = await resp.Content.ReadFromJsonAsync<StatementDraftSetEntrySplitDraftResultDto>(cancellationToken: ct);
        return result;
    }

    /// <summary>
    /// Status object returned by classification background endpoints for statement drafts.
    /// Contains progress information for the ongoing classification job.
    /// </summary>
    public sealed class StatementDraftsClassifyStatus
    {
        /// <summary>
        /// Indicates whether the classification job is currently running.
        /// </summary>
        public bool running { get; set; }

        /// <summary>
        /// Number of items that have already been processed by the job.
        /// </summary>
        public int processed { get; set; }

        /// <summary>
        /// Total number of items to process (may be zero when unknown).
        /// </summary>
        public int total { get; set; }

        /// <summary>
        /// Optional human-readable status message (progress or error information).
        /// </summary>
        public string? message { get; set; }
    }

    /// <summary>
    /// Starts the classification process for statement drafts.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Classification status information or null when the server returns an error.</returns>
    public async Task<StatementDraftsClassifyStatus?> StatementDrafts_StartClassifyAsync(CancellationToken ct = default)
    {
        var resp = await _http.PostAsync("/api/statement-drafts/classify", content: null, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.Accepted)
        {
            return await resp.Content.ReadFromJsonAsync<StatementDraftsClassifyStatus>(cancellationToken: ct);
        }
        if (resp.IsSuccessStatusCode)
        {
            return new StatementDraftsClassifyStatus { running = false, processed = 0, total = 0, message = null };
        }
        LastError = await resp.Content.ReadAsStringAsync(ct);
        return null;
    }

    /// <summary>
    /// Gets the status of the ongoing classification process for statement drafts.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Classification status DTO or null when the status cannot be retrieved.</returns>
    public async Task<StatementDraftsClassifyStatus?> StatementDrafts_GetClassifyStatusAsync(CancellationToken ct = default)
    {
        var resp = await _http.GetAsync("/api/statement-drafts/classify/status", ct);
        if (!resp.IsSuccessStatusCode) return null;
        return await resp.Content.ReadFromJsonAsync<StatementDraftsClassifyStatus>(cancellationToken: ct);
    }

    /// <summary>
    /// Starts the mass booking process for all statement drafts.
    /// </summary>
    /// <param name="ignoreWarnings">When true, ignores warnings during booking.</param>
    /// <param name="abortOnFirstIssue">When true, aborts the operation when an issue is detected.</param>
    /// <param name="bookEntriesIndividually">When true, books entries individually instead of in bulk.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Status DTO for the mass book operation or null when the server rejects the request.</returns>
    public async Task<StatementDraftMassBookStatusDto?> StatementDrafts_StartBookAllAsync(bool ignoreWarnings, bool abortOnFirstIssue, bool bookEntriesIndividually, CancellationToken ct = default)
    {
        var payload = new { ignoreWarnings, abortOnFirstIssue, bookEntriesIndividually };
        var resp = await _http.PostAsJsonAsync("/api/statement-drafts/book-all", payload, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.Accepted || resp.IsSuccessStatusCode)
        {
            return await resp.Content.ReadFromJsonAsync<StatementDraftMassBookStatusDto>(cancellationToken: ct);
        }
        LastError = await resp.Content.ReadAsStringAsync(ct);
        return null;
    }

    /// <summary>
    /// Gets the status of the ongoing mass booking process for statement drafts.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Mass book status DTO or null when unavailable.</returns>
    public async Task<StatementDraftMassBookStatusDto?> StatementDrafts_GetBookAllStatusAsync(CancellationToken ct = default)
    {
        var resp = await _http.GetAsync("/api/statement-drafts/book-all/status", ct);
        if (!resp.IsSuccessStatusCode) return null;
        return await resp.Content.ReadFromJsonAsync<StatementDraftMassBookStatusDto>(cancellationToken: ct);
    }

    /// <summary>
    /// Cancels the ongoing mass booking process for all statement drafts.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if the cancel request was accepted by the server; otherwise false.</returns>
    public async Task<bool> StatementDrafts_CancelBookAllAsync(CancellationToken ct = default)
    {
        var resp = await _http.PostAsync("/api/statement-drafts/book-all/cancel", content: null, ct);
        return resp.IsSuccessStatusCode;
    }

    #endregion Statement Drafts
}
