using System.Net;
using System.Net.Http.Json;
using System.Text.Json;


namespace FinanceManager.Shared;

/// <summary>
/// HTTP API client used by the UI to call server endpoints. Wraps an <see cref="HttpClient"/> and
/// provides convenience methods for the application's REST API surface.
/// </summary>
public partial class ApiClient : IApiClient
{
    private readonly HttpClient _http;

    /// <summary>
    /// Last error message extracted from the most recent failed HTTP response (if any).
    /// This is intended for UI display and diagnostics.
    /// </summary>
    public string? LastError { get; private set; }

    /// <summary>
    /// Optional machine-readable error code extracted from the most recent failed HTTP response.
    /// </summary>
    public string? LastErrorCode { get; private set; }

    /// <inheritdoc />
    public event EventHandler<ApiAuthenticationRequiredEventArgs>? AuthenticationRequired;

    /// <summary>
    /// Creates a new instance of <see cref="ApiClient"/> using the provided <see cref="HttpClient"/>.
    /// </summary>
    /// <param name="http">The configured HTTP client used to perform requests.</param>
    public ApiClient(HttpClient http)
    {
        _http = http;
    }

    /// <summary>
    /// Ensures the HTTP response indicates success. If not, attempts to extract structured error
    /// information (including RFC ProblemDetails style validation errors) and populate
    /// <see cref="LastError"/> and <see cref="LastErrorCode"/> before rethrowing via
    /// <see cref="HttpResponseMessage.EnsureSuccessStatusCode"/>.
    /// </summary>
    /// <param name="resp">The HTTP response message to inspect.</param>
    private async Task EnsureSuccessOrSetErrorAsync(HttpResponseMessage resp)
    {
        LastError = null;
        LastErrorCode = null;
        if (resp.IsSuccessStatusCode)
        {
            return;
        }

        try
        {
            var content = await resp.Content.ReadAsStringAsync();
            if (!string.IsNullOrWhiteSpace(content))
            {
                try
                {
                    using var doc = JsonDocument.Parse(content);
                    if (doc.RootElement.ValueKind == JsonValueKind.Object)
                    {
                        if (doc.RootElement.TryGetProperty("title", out var title) && title.ValueKind == JsonValueKind.String)
                        {
                            LastError = title.GetString();
                        }
                        if (doc.RootElement.TryGetProperty("detail", out var detail) && detail.ValueKind == JsonValueKind.String)
                        {
                            LastError = string.IsNullOrWhiteSpace(LastError) ? detail.GetString() : $"{LastError}: {detail.GetString()}";
                        }

                        // New standardized API error DTO: { origin, code, message }
                        if (doc.RootElement.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.String)
                        {
                            LastErrorCode = code.GetString();
                        }
                        if (doc.RootElement.TryGetProperty("message", out var msg) && msg.ValueKind == JsonValueKind.String)
                        {
                            LastError = msg.GetString();
                        }

                        // Legacy API error shape: { error, message }
                        if (doc.RootElement.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String)
                        {
                            LastErrorCode ??= e.GetString();
                        }

                        if (doc.RootElement.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Object)
                        {
                            SetRFCStyleError(errors);
                        }
                    }
                }
                catch
                {
                    LastError = content;
                }
            }
        }
        catch
        {
            // ignore
        }

        if (string.IsNullOrWhiteSpace(LastError))
        {
            LastError = resp.ReasonPhrase ?? $"HTTP {(int)resp.StatusCode}";
        }

        if (IsAuthenticationFailure(resp))
        {
            AuthenticationRequired?.Invoke(this, new ApiAuthenticationRequiredEventArgs(resp.StatusCode, LastErrorCode, LastError));
        }

        resp.EnsureSuccessStatusCode();
    }

    private bool IsAuthenticationFailure(HttpResponseMessage resp)
    {
        if (resp.StatusCode == HttpStatusCode.Unauthorized)
        {
            return true;
        }

        if (resp.StatusCode != HttpStatusCode.Forbidden)
        {
            return false;
        }

        if (resp.Headers.WwwAuthenticate.Any())
        {
            return true;
        }

        return IsAuthenticationErrorCode(LastErrorCode);
    }

    private static bool IsAuthenticationErrorCode(string? errorCode)
    {
        if (string.IsNullOrWhiteSpace(errorCode))
        {
            return false;
        }

        var normalized = new string(errorCode
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());

        return normalized is "authenticationrequired"
            or "authrequired"
            or "loginrequired"
            or "unauthenticated"
            or "unauthorized"
            or "invalidtoken"
            or "tokenexpired"
            or "expiredtoken"
            or "sessionexpired";
    }

    /// <summary>
    /// Extracts RFC ProblemDetails-style validation error messages from the provided "errors"
    /// JSON object and aggregates them into <see cref="LastError"/>.
    /// This method is best-effort and must not throw.
    /// </summary>
    /// <param name="errors">JSON element representing the errors object.</param>
    private void SetRFCStyleError(JsonElement errors)
    {
        var messages = new List<string>();
        foreach (var prop in errors.EnumerateObject())
        {
            try
            {
                if (prop.Value.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in prop.Value.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.String)
                        {
                            messages.Add($"{prop.Name}: {item.GetString()}");
                        }
                    }
                }
                else if (prop.Value.ValueKind == JsonValueKind.String)
                {
                    messages.Add($"{prop.Name}: {prop.Value.GetString()}");
                }
            }
            catch { /* best-effort */ }
        }

        if (messages.Count > 0)
        {
            LastError = string.Join("; ", messages);
        }
    }

    /// <summary>
    /// Checks whether any user accounts exist in the system.
    /// </summary>
    /// <param name="ct">Cancellation token used to cancel the HTTP request.</param>
    /// <returns><c>true</c> when at least one user exists; otherwise <c>false</c>.</returns>
    public async Task<bool> Users_HasAnyAsync(CancellationToken ct = default)
    {
        var resp = await _http.GetAsync("/api/users/exists", ct);
        await EnsureSuccessOrSetErrorAsync(resp);
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);

        if (json.ValueKind == JsonValueKind.True || json.ValueKind == JsonValueKind.False)
        {
            return json.GetBoolean();
        }

        if (json.ValueKind == JsonValueKind.Object)
        {
            if (json.TryGetProperty("any", out var prop))
            {
                if (prop.ValueKind == JsonValueKind.True || prop.ValueKind == JsonValueKind.False)
                {
                    return prop.GetBoolean();
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Lists budget rules that apply to a budget category.
    /// </summary>
    /// <param name="budgetCategoryId">The budget category id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    public async Task<IReadOnlyList<FinanceManager.Shared.Dtos.Budget.BudgetRuleDto>> Budgets_ListRulesByCategoryAsync(Guid budgetCategoryId, CancellationToken ct = default)
    {
        var resp = await _http.GetAsync($"/api/budget/rules/by-category/{budgetCategoryId}", ct);
        await EnsureSuccessOrSetErrorAsync(resp);
        var list = await resp.Content.ReadFromJsonAsync<IReadOnlyList<FinanceManager.Shared.Dtos.Budget.BudgetRuleDto>>(cancellationToken: ct);
        return list ?? Array.Empty<FinanceManager.Shared.Dtos.Budget.BudgetRuleDto>();
    }

    // StatementDrafts batch update method moved to ApiClient.StatementDrafts.cs
}
