using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using FinanceManager.Shared.Dtos;

namespace FinanceManager.Web.Services
{
    /// <summary>
    /// Client wrapper for server API endpoints. Can be registered as a typed HTTP client via DI
    /// or constructed directly with an existing <see cref="HttpClient"/> (useful in tests).
    /// </summary>
    public interface IApiClient
    {
        /// <summary>
        /// Returns a list of supported holiday country codes.
        /// </summary>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>Array of country codes or null.</returns>
        Task<string[]?> GetHolidayCountriesAsync(CancellationToken ct = default);

        /// <summary>
        /// Returns a list of configured holiday providers.
        /// </summary>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>Array of provider identifiers or null.</returns>
        Task<string[]?> GetHolidayProvidersAsync(CancellationToken ct = default);

        /// <summary>
        /// Returns subdivisions for the given holiday provider and country.
        /// </summary>
        /// <param name="provider">Provider identifier.</param>
        /// <param name="country">ISO country code.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>Array of subdivision identifiers or null.</returns>
        Task<string[]?> GetHolidaySubdivisionsAsync(string provider, string country, CancellationToken ct = default);

        /// <summary>
        /// Retrieves aggregated time series points across all accounts owned by the current user.
        /// </summary>
        /// <param name="period">Aggregation period name (e.g. "Month").</param>
        /// <param name="take">Maximum number of points to return.</param>
        /// <param name="maxYearsBack">Optional cap for how many years back to include.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>Array of <see cref="TimeSeriesPointDto"/> or null.</returns>
        Task<TimeSeriesPointDto[]?> GetAccountsAggregatesAllAsync(string period = "Month", int take = 36, int? maxYearsBack = null, CancellationToken ct = default);

        /// <summary>
        /// Retrieves aggregated time series points for a single account.
        /// </summary>
        Task<TimeSeriesPointDto[]?> GetAccountAggregatesAsync(System.Guid accountId, string period = "Month", int take = 36, int? maxYearsBack = null, CancellationToken ct = default);

        /// <summary>
        /// Retrieves aggregated time series points for a single contact.
        /// </summary>
        Task<TimeSeriesPointDto[]?> GetContactAggregatesAsync(System.Guid contactId, string period = "Month", int take = 36, int? maxYearsBack = null, CancellationToken ct = default);

        /// <summary>
        /// Retrieves aggregated time series points for a single savings plan.
        /// </summary>
        Task<TimeSeriesPointDto[]?> GetSavingsPlanAggregatesAsync(System.Guid planId, string period = "Month", int take = 36, int? maxYearsBack = null, CancellationToken ct = default);

        /// <summary>
        /// Retrieves aggregated time series points across all savings plans owned by the current user.
        /// </summary>
        Task<TimeSeriesPointDto[]?> GetSavingsPlansAggregatesAllAsync(string period = "Month", int take = 36, int? maxYearsBack = null, CancellationToken ct = default);

        /// <summary>
        /// Retrieves aggregated time series points for a single security.
        /// </summary>
        Task<TimeSeriesPointDto[]?> GetSecurityAggregatesAsync(System.Guid securityId, string period = "Month", int take = 36, int? maxYearsBack = null, CancellationToken ct = default);

        /// <summary>
        /// Retrieves aggregated dividend time series for securities owned by the current user.
        /// </summary>
        Task<TimeSeriesPointDto[]?> GetSecuritiesDividendsAsync(string period = "Quarter", int take = 36, int? maxYearsBack = null, CancellationToken ct = default);
    }

    /// <summary>
    /// Default implementation of <see cref="IApiClient"/> using an injected <see cref="HttpClient"/>.
    /// </summary>
    public sealed class ApiClient : IApiClient
    {
        private readonly HttpClient _client;

        /// <summary>
        /// Constructs a new <see cref="ApiClient"/>.
        /// </summary>
        /// <param name="client">Preconfigured HTTP client to use for requests.</param>
        public ApiClient(HttpClient client)
        {
            _client = client;
        }

        /// <inheritdoc />
        public Task<string[]?> GetHolidayCountriesAsync(CancellationToken ct = default)
            => _client.GetFromJsonAsync<string[]>("api/meta/holiday-countries", ct);

        /// <inheritdoc />
        public Task<string[]?> GetHolidayProvidersAsync(CancellationToken ct = default)
            => _client.GetFromJsonAsync<string[]>("api/meta/holiday-providers", ct);

        /// <inheritdoc />
        public Task<string[]?> GetHolidaySubdivisionsAsync(string provider, string country, CancellationToken ct = default)
        {
            var url = $"api/meta/holiday-subdivisions?provider={System.Uri.EscapeDataString(provider)}&country={System.Uri.EscapeDataString(country)}";
            return _client.GetFromJsonAsync<string[]>(url, ct);
        }

        /// <inheritdoc />
        public Task<TimeSeriesPointDto[]?> GetAccountsAggregatesAllAsync(string period = "Month", int take = 36, int? maxYearsBack = null, CancellationToken ct = default)
        {
            var url = $"api/accounts/aggregates?period={System.Uri.EscapeDataString(period)}&take={take}";
            if (maxYearsBack.HasValue)
            {
                url += $"&maxYearsBack={maxYearsBack.Value}";
            }
            return _client.GetFromJsonAsync<TimeSeriesPointDto[]>(url, ct);
        }

        /// <inheritdoc />
        public Task<TimeSeriesPointDto[]?> GetAccountAggregatesAsync(System.Guid accountId, string period = "Month", int take = 36, int? maxYearsBack = null, CancellationToken ct = default)
        {
            var url = $"api/accounts/{accountId}/aggregates?period={System.Uri.EscapeDataString(period)}&take={take}";
            if (maxYearsBack.HasValue) url += $"&maxYearsBack={maxYearsBack.Value}";
            return _client.GetFromJsonAsync<TimeSeriesPointDto[]>(url, ct);
        }

        /// <inheritdoc />
        public Task<TimeSeriesPointDto[]?> GetContactAggregatesAsync(System.Guid contactId, string period = "Month", int take = 36, int? maxYearsBack = null, CancellationToken ct = default)
        {
            var url = $"api/contacts/{contactId}/aggregates?period={System.Uri.EscapeDataString(period)}&take={take}";
            if (maxYearsBack.HasValue) url += $"&maxYearsBack={maxYearsBack.Value}";
            return _client.GetFromJsonAsync<TimeSeriesPointDto[]>(url, ct);
        }

        /// <inheritdoc />
        public Task<TimeSeriesPointDto[]?> GetSavingsPlanAggregatesAsync(System.Guid planId, string period = "Month", int take = 36, int? maxYearsBack = null, CancellationToken ct = default)
        {
            var url = $"api/savings-plans/{planId}/aggregates?period={System.Uri.EscapeDataString(period)}&take={take}";
            if (maxYearsBack.HasValue) url += $"&maxYearsBack={maxYearsBack.Value}";
            return _client.GetFromJsonAsync<TimeSeriesPointDto[]>(url, ct);
        }

        /// <inheritdoc />
        public Task<TimeSeriesPointDto[]?> GetSavingsPlansAggregatesAllAsync(string period = "Month", int take = 36, int? maxYearsBack = null, CancellationToken ct = default)
        {
            var url = $"api/savings-plans/aggregates?period={System.Uri.EscapeDataString(period)}&take={take}";
            if (maxYearsBack.HasValue) url += $"&maxYearsBack={maxYearsBack.Value}";
            return _client.GetFromJsonAsync<TimeSeriesPointDto[]>(url, ct);
        }

        /// <inheritdoc />
        public Task<TimeSeriesPointDto[]?> GetSecurityAggregatesAsync(System.Guid securityId, string period = "Month", int take = 36, int? maxYearsBack = null, CancellationToken ct = default)
        {
            var url = $"api/securities/{securityId}/aggregates?period={System.Uri.EscapeDataString(period)}&take={take}";
            if (maxYearsBack.HasValue) url += $"&maxYearsBack={maxYearsBack.Value}";
            return _client.GetFromJsonAsync<TimeSeriesPointDto[]>(url, ct);
        }

        /// <inheritdoc />
        public Task<TimeSeriesPointDto[]?> GetSecuritiesDividendsAsync(string period = "Quarter", int take = 36, int? maxYearsBack = null, CancellationToken ct = default)
        {
            var url = $"api/securities/dividends?period={System.Uri.EscapeDataString(period)}&take={take}";
            if (maxYearsBack.HasValue) url += $"&maxYearsBack={maxYearsBack.Value}";
            return _client.GetFromJsonAsync<TimeSeriesPointDto[]>(url, ct);
        }
    }
}
