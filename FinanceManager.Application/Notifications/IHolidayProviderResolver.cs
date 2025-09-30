using FinanceManager.Domain.Notifications;

namespace FinanceManager.Application.Notifications;

public interface IHolidayProviderResolver
{
    IHolidayProvider Resolve(HolidayProviderKind kind);
}
