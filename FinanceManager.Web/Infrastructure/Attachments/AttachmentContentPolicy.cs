using System.Net.Mime;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace FinanceManager.Web.Infrastructure.Attachments;

/// <summary>
/// Validates attachment content server-side and normalizes MIME types for upload and download.
/// </summary>
public interface IAttachmentContentPolicy
{
    /// <summary>
    /// Validates the uploaded file content and returns a reset stream plus normalized content type.
    /// </summary>
    /// <param name="file">The file.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    Task<AttachmentContentValidationResult> ValidateUploadAsync(IFormFile file, CancellationToken ct);

    /// <summary>
    /// Normalizes a stored content type to a type that is safe to send in a download response.
    /// </summary>
    /// <param name="storedContentType">The stored content type.</param>
    /// <returns>The result.</returns>
    string NormalizeDownloadContentType(string? storedContentType);
}

/// <summary>
/// Server-side attachment content policy.
/// </summary>
public sealed class AttachmentContentPolicy : IAttachmentContentPolicy
{
    private static readonly Encoding StrictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly Regex SvgRootRegex = new(@"<\s*svg(?:\s|>|:)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex SvgActiveContentRegex = new(@"<\s*script(?:\s|>)|\son[a-z]+\s*=|(?:href|xlink:href)\s*=\s*[""']\s*(?:https?:|//|data:|javascript:)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private readonly AttachmentUploadOptions _options;

    /// <summary>
    /// Initializes a new instance of <see cref="AttachmentContentPolicy"/>.
    /// </summary>
    /// <param name="options">The options.</param>
    public AttachmentContentPolicy(IOptions<AttachmentUploadOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
    }

    /// <inheritdoc />
    public async Task<AttachmentContentValidationResult> ValidateUploadAsync(IFormFile file, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(file);

        var buffer = new MemoryStream(capacity: file.Length > int.MaxValue ? 0 : (int)file.Length);
        await using (var input = file.OpenReadStream())
        {
            await input.CopyToAsync(buffer, ct);
        }

        var bytes = buffer.ToArray();
        var detected = DetectContentType(bytes, file.FileName, file.ContentType);
        if (detected is null)
        {
            buffer.Dispose();
            return AttachmentContentValidationResult.Reject("Err_Invalid_ContentType", "Unsupported or invalid file content.");
        }

        if (!IsAllowed(detected))
        {
            buffer.Dispose();
            return AttachmentContentValidationResult.Reject("Err_Invalid_ContentType", $"Unsupported content type '{detected}'.");
        }

        var clientContentType = NormalizeContentType(file.ContentType);
        if (!string.IsNullOrWhiteSpace(clientContentType)
            && IsAllowed(clientContentType)
            && !IsCompatibleClientType(clientContentType, detected))
        {
            buffer.Dispose();
            return AttachmentContentValidationResult.Reject("Err_Invalid_ContentType", $"Unsupported content type '{clientContentType}'.");
        }

        buffer.Position = 0;
        return AttachmentContentValidationResult.Accept(buffer, detected);
    }

    /// <inheritdoc />
    public string NormalizeDownloadContentType(string? storedContentType)
    {
        var normalized = NormalizeContentType(storedContentType);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return MediaTypeNames.Application.Octet;
        }

        return normalized switch
        {
            "application/pdf" => normalized,
            "image/png" => normalized,
            "image/jpeg" => normalized,
            "image/svg+xml" => normalized,
            "image/x-icon" => normalized,
            "image/vnd.microsoft.icon" => normalized,
            "text/plain" => normalized,
            "text/csv" => normalized,
            "application/zip" => normalized,
            _ => MediaTypeNames.Application.Octet
        };
    }

    private string? DetectContentType(byte[] bytes, string fileName, string? clientContentType)
    {
        if (StartsWith(bytes, "%PDF-"u8))
        {
            return "application/pdf";
        }

        if (StartsWith(bytes, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]))
        {
            return "image/png";
        }

        if (StartsWith(bytes, [0xFF, 0xD8, 0xFF]))
        {
            return "image/jpeg";
        }

        if (StartsWith(bytes, [0x50, 0x4B, 0x03, 0x04])
            || StartsWith(bytes, [0x50, 0x4B, 0x05, 0x06])
            || StartsWith(bytes, [0x50, 0x4B, 0x07, 0x08]))
        {
            return "application/zip";
        }

        if (StartsWith(bytes, [0x00, 0x00, 0x01, 0x00]))
        {
            return IsAllowed("image/vnd.microsoft.icon") ? "image/vnd.microsoft.icon" : "image/x-icon";
        }

        if (!TryReadSafeText(bytes, out var text))
        {
            return null;
        }

        var extension = Path.GetExtension(fileName);
        var normalizedClient = NormalizeContentType(clientContentType);
        if (IsSafeSvg(text)
            && (string.Equals(extension, ".svg", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalizedClient, "image/svg+xml", StringComparison.OrdinalIgnoreCase)))
        {
            return "image/svg+xml";
        }

        if (string.Equals(extension, ".csv", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalizedClient, "text/csv", StringComparison.OrdinalIgnoreCase))
        {
            return "text/csv";
        }

        if (string.Equals(extension, ".txt", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalizedClient, "text/plain", StringComparison.OrdinalIgnoreCase)
            || IsAllowed("text/plain"))
        {
            return "text/plain";
        }

        return null;
    }

    private static bool TryReadSafeText(byte[] bytes, out string text)
    {
        text = string.Empty;
        try
        {
            text = StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return false;
        }

        foreach (var ch in text)
        {
            if (ch == '\0')
            {
                return false;
            }

            if (char.IsControl(ch) && ch is not '\r' and not '\n' and not '\t' and not '\f')
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsSafeSvg(string text)
        => SvgRootRegex.IsMatch(text) && !SvgActiveContentRegex.IsMatch(text);

    private bool IsAllowed(string contentType)
        => _options.AllowedMimeTypes.Contains(contentType, StringComparer.OrdinalIgnoreCase);

    private static bool IsCompatibleClientType(string clientContentType, string detectedContentType)
    {
        if (string.Equals(clientContentType, detectedContentType, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return IsIconType(clientContentType) && IsIconType(detectedContentType);
    }

    private static bool IsIconType(string contentType)
        => string.Equals(contentType, "image/x-icon", StringComparison.OrdinalIgnoreCase)
           || string.Equals(contentType, "image/vnd.microsoft.icon", StringComparison.OrdinalIgnoreCase);

    private static string? NormalizeContentType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return null;
        }

        var semicolon = contentType.IndexOf(';', StringComparison.Ordinal);
        var value = semicolon >= 0 ? contentType[..semicolon] : contentType;
        return value.Trim().ToLowerInvariant();
    }

    private static bool StartsWith(byte[] source, ReadOnlySpan<byte> prefix)
        => source.AsSpan().StartsWith(prefix);
}

/// <summary>
/// Result of attachment upload content validation.
/// </summary>
/// <param name="IsAllowed">The is allowed.</param>
/// <param name="ContentType">The content type.</param>
/// <param name="Content">The content.</param>
/// <param name="ErrorCode">The error code.</param>
/// <param name="ErrorMessage">The error message.</param>
/// <returns>The result.</returns>
public sealed record AttachmentContentValidationResult(
    bool IsAllowed,
    string? ContentType,
    Stream? Content,
    string ErrorCode,
    string ErrorMessage)
{
    /// <summary>
    /// Creates an accepted result.
    /// </summary>
    /// <param name="content">The content.</param>
    /// <param name="contentType">The content type.</param>
    /// <returns>The result.</returns>
    public static AttachmentContentValidationResult Accept(Stream content, string contentType)
        => new(true, contentType, content, string.Empty, string.Empty);

    /// <summary>
    /// Creates a rejected result.
    /// </summary>
    /// <param name="errorCode">The error code.</param>
    /// <param name="errorMessage">The error message.</param>
    /// <returns>The result.</returns>
    public static AttachmentContentValidationResult Reject(string errorCode, string errorMessage)
        => new(false, null, null, errorCode, errorMessage);
}
