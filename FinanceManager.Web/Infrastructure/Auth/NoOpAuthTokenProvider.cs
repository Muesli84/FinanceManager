public sealed class NoOpAuthTokenProvider : IAuthTokenProvider
{
    public Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        // Placeholder: liefert kein Token. Ersetzen durch echte Beschaffung (z.B. aus JWT-Issuer).
        return Task.FromResult<string?>(null);
    }
}