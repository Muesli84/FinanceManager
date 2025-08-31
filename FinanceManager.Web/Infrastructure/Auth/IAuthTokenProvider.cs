using System.Threading;
using System.Threading.Tasks;

public interface IAuthTokenProvider
{
    Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken);
}