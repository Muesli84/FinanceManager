namespace FinanceManager.Shared.Dtos;

/// <summary>
/// DTO containing a short-lived download token that allows anonymous download of an attachment.
/// </summary>
/// <param name="Token">A protected token string that encodes attachment id, owner id and expiry.</param>
public sealed record AttachmentDownloadTokenDto(string Token);
