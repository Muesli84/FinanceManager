using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace FinanceManager.Web.Infrastructure;

/// <summary>
/// Streams a response body using a provided async callback, setting Content-Type and Content-Disposition.
/// Avoids manual Response usage in controllers while supporting large streaming writes.
/// </summary>
public sealed class StreamCallbackResult : IActionResult
{
    private readonly string _contentType;
    private readonly Func<Stream, CancellationToken, Task> _callback;

    public string? FileDownloadName { get; init; }

    public StreamCallbackResult(string contentType, Func<Stream, CancellationToken, Task> callback)
    {
        _contentType = contentType ?? throw new ArgumentNullException(nameof(contentType));
        _callback = callback ?? throw new ArgumentNullException(nameof(callback));
    }

    public async Task ExecuteResultAsync(ActionContext context)
    {
        var response = context.HttpContext.Response;
        response.ContentType = _contentType;

        if (!string.IsNullOrWhiteSpace(FileDownloadName))
        {
            // RFC 6266: provide ASCII fallback filename plus UTF-8 encoded filename*
            var ascii = ToAscii(FileDownloadName!);
            var utf8Star = Uri.EscapeDataString(FileDownloadName!);
            var cd = $"attachment; filename=\"{ascii}\"; filename*=UTF-8''{utf8Star}";
            response.Headers[HeaderNames.ContentDisposition] = cd;
        }

        await _callback(response.Body, context.HttpContext.RequestAborted);
    }

    private static string ToAscii(string name)
    {
        if (string.IsNullOrEmpty(name)) { return string.Empty; }
        Span<char> buffer = stackalloc char[name.Length];
        var i = 0;
        foreach (var ch in name)
        {
            buffer[i++] = ch <= 0x7F ? ch : '_';
        }
        return new string(buffer[..i]);
    }
}
