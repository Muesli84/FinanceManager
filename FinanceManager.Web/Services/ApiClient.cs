using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;

namespace FinanceManager.Web.Services
{
    /// <summary>
    /// Client wrapper for server API endpoints. Can be registered as a typed HTTP client via DI
    /// or constructed directly with an existing HttpClient (useful in tests).
    /// </summary>
    public interface IApiClient
    {
        Task<string[]?> GetHolidayCountriesAsync(CancellationToken ct = default);
        Task<string[]?> GetHolidayProvidersAsync(CancellationToken ct = default);
        Task<string[]?> GetHolidaySubdivisionsAsync(string provider, string country, CancellationToken ct = default);
    }

    public sealed class ApiClient : IApiClient
    {
        private readonly HttpClient _client;

        public ApiClient(HttpClient client)
        {
            _client = client;
        }

        public Task<string[]?> GetHolidayCountriesAsync(CancellationToken ct = default)
            => _client.GetFromJsonAsync<string[]>("api/meta/holiday-countries", ct);

        public Task<string[]?> GetHolidayProvidersAsync(CancellationToken ct = default)
            => _client.GetFromJsonAsync<string[]>("api/meta/holiday-providers", ct);

        public Task<string[]?> GetHolidaySubdivisionsAsync(string provider, string country, CancellationToken ct = default)
        {
            var url = $"api/meta/holiday-subdivisions?provider={System.Uri.EscapeDataString(provider)}&country={System.Uri.EscapeDataString(country)}";
            return _client.GetFromJsonAsync<string[]>(url, ct);
        }
    }
}
