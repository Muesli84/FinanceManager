public interface IAuthTokenProvider
{
    Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken);
}