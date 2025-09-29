using FinanceManager.Application.Notifications;
using FinanceManager.Domain.Notifications;
using Microsoft.Extensions.DependencyInjection;

namespace FinanceManager.Infrastructure.Notifications;

public sealed class HolidayProviderResolver : IHolidayProviderResolver
{
    private readonly IServiceProvider _sp;

    public HolidayProviderResolver(IServiceProvider sp)
    {
        _sp = sp;
    }

    public IHolidayProvider Resolve(HolidayProviderKind kind)
    {
        return kind switch
        {
            HolidayProviderKind.NagerDate => _sp.GetRequiredService<NagerDateHolidayProvider>(),
            _ => _sp.GetRequiredService<InMemoryHolidayProvider>()
        };
    }
}
