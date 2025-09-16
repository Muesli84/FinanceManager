using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

public sealed class AuthenticatedHttpClientHandler : DelegatingHandler
{
    private readonly IAuthTokenProvider _tokenProvider;

    public AuthenticatedHttpClientHandler(IAuthTokenProvider tokenProvider)
    {
        _tokenProvider = tokenProvider;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = await _tokenProvider.GetAccessTokenAsync(cancellationToken);
        if (!string.IsNullOrEmpty(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return await base.SendAsync(request, cancellationToken);
    }
}