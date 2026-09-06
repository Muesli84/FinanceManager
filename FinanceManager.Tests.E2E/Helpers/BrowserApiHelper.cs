using System.Text.Json;
using Microsoft.Playwright;

namespace FinanceManager.Tests.E2E;

/// <summary>
/// Calls the application's JSON API from inside a Playwright browser page via <c>fetch</c>, so requests
/// carry the browser's session cookie automatically - the standard way E2E tests set up state or assert
/// server-side results without going through the UI for every step.
/// </summary>
public static class BrowserApiHelper
{
    /// <summary>
    /// Result of an API call made through the browser, capturing the HTTP status alongside the
    /// deserialized value (or the raw body, if deserialization isn't applicable) so callers can assert on
    /// both success and failure responses.
    /// </summary>
    /// <typeparam name="T">Type the response body deserializes to.</typeparam>
    /// <param name="Status">HTTP status code of the response.</param>
    /// <param name="Value">Deserialized response body, or default if it could not be deserialized.</param>
    /// <param name="Raw">Raw response body text.</param>
    /// <returns>The result.</returns>
    public sealed record BrowserApiResponse<T>(int Status, T? Value, string? Raw);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// POSTs JSON via the browser and deserializes the JSON response body, throwing if the request failed.
    /// </summary>
    /// <typeparam name="TRequest">Type of the request payload.</typeparam>
    /// <typeparam name="TResponse">Type the response body deserializes to.</typeparam>
    /// <param name="page">Browser page to issue the request from.</param>
    /// <param name="path">Relative API path to POST to.</param>
    /// <param name="payload">Request payload, serialized as JSON.</param>
    /// <returns>The deserialized response body.</returns>
    public static async Task<TResponse> PostJsonAsync<TRequest, TResponse>(IPage page, string path, TRequest payload)
    {
        var payloadJson = JsonSerializer.Serialize(payload, JsonOptions);
        var json = await page.EvaluateAsync<string>("""
            async ({ path, payloadJson }) => {
                const response = await fetch(path, {
                    method: 'POST',
                    credentials: 'include',
                    headers: { 'Content-Type': 'application/json' },
                    body: payloadJson
                });

                if (!response.ok) {
                    throw new Error(await response.text());
                }

                return JSON.stringify(await response.json());
            }
            """, new { path, payloadJson });

        var value = JsonSerializer.Deserialize<TResponse>(json, JsonOptions);
        return value ?? throw new InvalidOperationException($"Unable to deserialize response for {typeof(TResponse).Name}.");
    }

    /// <summary>
    /// POSTs JSON via the browser and throws if the response was not a 2xx status; use this overload when
    /// the response body isn't needed.
    /// </summary>
    /// <typeparam name="TRequest">Type of the request payload.</typeparam>
    /// <param name="page">Browser page to issue the request from.</param>
    /// <param name="path">Relative API path to POST to.</param>
    /// <param name="payload">Request payload, serialized as JSON.</param>
    public static async Task PostJsonAsync<TRequest>(IPage page, string path, TRequest payload)
    {
        var result = await SendJsonAsync<TRequest>(page, "POST", path, payload);
        if (result.Status < 200 || result.Status >= 300)
        {
            throw new InvalidOperationException(result.Raw ?? $"Request to {path} failed with status {result.Status}.");
        }
    }

    /// <summary>
    /// GETs JSON via the browser and deserializes the response, throwing if the request failed or the
    /// body was empty.
    /// </summary>
    /// <typeparam name="TResponse">Type the response body deserializes to.</typeparam>
    /// <param name="page">Browser page to issue the request from.</param>
    /// <param name="path">Relative API path to GET.</param>
    /// <returns>The deserialized response body.</returns>
    public static async Task<TResponse> GetJsonAsync<TResponse>(IPage page, string path)
    {
        var result = await SendWithoutBodyAsync<TResponse>(page, "GET", path);
        if (result.Status < 200 || result.Status >= 300 || result.Value is null)
        {
            throw new InvalidOperationException(result.Raw ?? $"GET {path} failed with status {result.Status}.");
        }

        return result.Value;
    }

    /// <summary>
    /// PUTs JSON via the browser and deserializes the response, throwing if the request failed or the
    /// body was empty.
    /// </summary>
    /// <typeparam name="TRequest">Type of the request payload.</typeparam>
    /// <typeparam name="TResponse">Type the response body deserializes to.</typeparam>
    /// <param name="page">Browser page to issue the request from.</param>
    /// <param name="path">Relative API path to PUT to.</param>
    /// <param name="payload">Request payload, serialized as JSON.</param>
    /// <returns>The deserialized response body.</returns>
    public static async Task<TResponse> PutJsonAsync<TRequest, TResponse>(IPage page, string path, TRequest payload)
    {
        var result = await SendJsonAsync<TRequest, TResponse>(page, "PUT", path, payload);
        if (result.Status < 200 || result.Status >= 300 || result.Value is null)
        {
            throw new InvalidOperationException(result.Raw ?? $"PUT {path} failed with status {result.Status}.");
        }

        return result.Value;
    }

    /// <summary>
    /// POSTs JSON via the browser and returns the status code alongside the (possibly failed) response,
    /// without throwing - use this when a test needs to assert on an expected error status.
    /// </summary>
    /// <typeparam name="TRequest">Type of the request payload.</typeparam>
    /// <typeparam name="TResponse">Type the response body deserializes to.</typeparam>
    /// <param name="page">Browser page to issue the request from.</param>
    /// <param name="path">Relative API path to POST to.</param>
    /// <param name="payload">Request payload, serialized as JSON.</param>
    /// <returns>The status code and (if deserializable) the response body.</returns>
    public static Task<BrowserApiResponse<TResponse>> PostJsonWithStatusAsync<TRequest, TResponse>(IPage page, string path, TRequest payload)
        => SendJsonAsync<TRequest, TResponse>(page, "POST", path, payload);

    /// <summary>
    /// POSTs with no body via the browser and returns the status code alongside the response, without
    /// throwing on failure.
    /// </summary>
    /// <typeparam name="TResponse">Type the response body deserializes to.</typeparam>
    /// <param name="page">Browser page to issue the request from.</param>
    /// <param name="path">Relative API path to POST to.</param>
    /// <returns>The status code and (if deserializable) the response body.</returns>
    public static Task<BrowserApiResponse<TResponse>> PostWithStatusAsync<TResponse>(IPage page, string path)
        => SendWithoutBodyAsync<TResponse>(page, "POST", path);

    /// <summary>
    /// GETs via the browser and returns the status code alongside the response, without throwing on
    /// failure.
    /// </summary>
    /// <typeparam name="TResponse">Type the response body deserializes to.</typeparam>
    /// <param name="page">Browser page to issue the request from.</param>
    /// <param name="path">Relative API path to GET.</param>
    /// <returns>The status code and (if deserializable) the response body.</returns>
    public static Task<BrowserApiResponse<TResponse>> GetWithStatusAsync<TResponse>(IPage page, string path)
        => SendWithoutBodyAsync<TResponse>(page, "GET", path);

    /// <summary>
    /// DELETEs via the browser and returns just the resulting status code.
    /// </summary>
    /// <param name="page">Browser page to issue the request from.</param>
    /// <param name="path">Relative API path to DELETE.</param>
    /// <returns>The response's HTTP status code.</returns>
    public static async Task<int> DeleteAsync(IPage page, string path)
    {
        var raw = await SendRawAsync(page, "DELETE", path, null);
        return raw.Status;
    }

    /// <summary>
    /// POSTs with no request or response body via the browser, throwing if the request failed.
    /// </summary>
    /// <param name="page">Browser page to issue the request from.</param>
    /// <param name="path">Relative API path to POST to.</param>
    public static async Task PostNoContentAsync(IPage page, string path)
    {
        var status = await page.EvaluateAsync<int>("""
            async (path) => {
                const response = await fetch(path, { method: 'POST', credentials: 'include' });
                return response.status;
            }
            """, path);

        if (status < 200 || status >= 300)
        {
            throw new InvalidOperationException($"Request to {path} failed with status {status}.");
        }
    }

    /// <summary>
    /// Uploads a file as multipart form data via the browser (building the <c>FormData</c> client-side
    /// from a base64-encoded byte array, since Playwright's page context has no direct binary bridge), with
    /// optional additional form fields alongside the file.
    /// </summary>
    /// <typeparam name="TResponse">Type the response body deserializes to.</typeparam>
    /// <param name="page">Browser page to issue the request from.</param>
    /// <param name="path">Relative API path to POST to.</param>
    /// <param name="fileName">File name reported in the multipart upload.</param>
    /// <param name="contentType">MIME content type reported for the uploaded file.</param>
    /// <param name="content">Raw file bytes to upload.</param>
    /// <param name="additionalFormFields">Optional extra form fields to send alongside the file.</param>
    /// <returns>The deserialized response body.</returns>
    public static async Task<TResponse> PostMultipartAsync<TResponse>(
        IPage page,
        string path,
        string fileName,
        string contentType,
        byte[] content,
        IReadOnlyDictionary<string, string>? additionalFormFields = null)
    {
        var json = await page.EvaluateAsync<string>("""
            async ({ path, fileName, contentType, contentBase64, additionalFormFields }) => {
                const binary = atob(contentBase64);
                const bytes = new Uint8Array(binary.length);
                for (let i = 0; i < binary.length; i++) {
                    bytes[i] = binary.charCodeAt(i);
                }

                const form = new FormData();
                form.append('file', new File([bytes], fileName, { type: contentType }));
                if (additionalFormFields) {
                    for (const [key, value] of Object.entries(additionalFormFields)) {
                        form.append(key, value);
                    }
                }

                const response = await fetch(path, {
                    method: 'POST',
                    credentials: 'include',
                    body: form
                });

                if (!response.ok) {
                    throw new Error(await response.text());
                }

                return JSON.stringify(await response.json());
            }
            """, new
        {
            path,
            fileName,
            contentType,
            contentBase64 = Convert.ToBase64String(content),
            additionalFormFields
        });

        var value = JsonSerializer.Deserialize<TResponse>(json, JsonOptions);
        return value ?? throw new InvalidOperationException($"Unable to deserialize response for {typeof(TResponse).Name}.");
    }

    private static async Task<BrowserApiResponse<TResponse>> SendJsonAsync<TRequest, TResponse>(IPage page, string method, string path, TRequest payload)
    {
        var payloadJson = JsonSerializer.Serialize(payload, JsonOptions);
        var raw = await SendRawAsync(page, method, path, payloadJson);
        var value = DeserializeBody<TResponse>(raw.Text);
        return new BrowserApiResponse<TResponse>(raw.Status, value, raw.Text);
    }

    private static async Task<BrowserApiResponse<object?>> SendJsonAsync<TRequest>(IPage page, string method, string path, TRequest payload)
    {
        var payloadJson = JsonSerializer.Serialize(payload, JsonOptions);
        var raw = await SendRawAsync(page, method, path, payloadJson);
        return new BrowserApiResponse<object?>(raw.Status, null, raw.Text);
    }

    private static async Task<BrowserApiResponse<TResponse>> SendWithoutBodyAsync<TResponse>(IPage page, string method, string path)
    {
        var raw = await SendRawAsync(page, method, path, null);
        var value = DeserializeBody<TResponse>(raw.Text);
        return new BrowserApiResponse<TResponse>(raw.Status, value, raw.Text);
    }

    private static async Task<BrowserRawResponse> SendRawAsync(IPage page, string method, string path, string? payloadJson)
    {
        var json = await page.EvaluateAsync<string>("""
            async ({ method, path, payloadJson }) => {
                const options = {
                    method,
                    credentials: 'include',
                    headers: { 'Content-Type': 'application/json' }
                };

                if (payloadJson !== null) {
                    options.body = payloadJson;
                }

                const response = await fetch(path, options);
                const text = await response.text();
                return JSON.stringify({ status: response.status, text });
            }
            """, new { method, path, payloadJson });

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        return new BrowserRawResponse(
            root.GetProperty("status").GetInt32(),
            root.GetProperty("text").GetString() ?? string.Empty);
    }

    private static TResponse? DeserializeBody<TResponse>(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return default;
        }

        return JsonSerializer.Deserialize<TResponse>(raw, JsonOptions);
    }

    private sealed record BrowserRawResponse(int Status, string Text);
}
