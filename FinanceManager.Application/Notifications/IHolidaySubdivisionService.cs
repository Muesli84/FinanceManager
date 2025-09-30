using FinanceManager.Domain.Notifications;

namespace FinanceManager.Application.Notifications;

public interface IHolidaySubdivisionService
{
    Task<string[]> GetSubdivisionsAsync(HolidayProviderKind provider, string countryCode, CancellationToken ct);
}
