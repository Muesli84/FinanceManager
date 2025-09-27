using System.Net;
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
        // Do not tie token retrieval to request cancellation to avoid spurious cancellations during navigation
        string? token = null;
        try
        {
            token = await _tokenProvider.GetAccessTokenAsync(CancellationToken.None);
        }
        catch
        {
            // ignore token retrieval errors; proceed without auth header
        }

        if (!string.IsNullOrEmpty(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        try
        {
            return await base.SendAsync(request, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Convert client-triggered cancellations to a synthetic HTTP response to avoid breaking the debugger
            var resp = new HttpResponseMessage((HttpStatusCode)499)
            {
                RequestMessage = request,
                ReasonPhrase = "Client Closed Request"
            };
            return resp;
        }
    }
}