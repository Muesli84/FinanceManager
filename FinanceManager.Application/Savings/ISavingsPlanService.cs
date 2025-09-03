using FinanceManager.Domain.Savings;
using FinanceManager.Shared.Dtos;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace FinanceManager.Application.Savings;

public interface ISavingsPlanService
{
    Task<IReadOnlyList<SavingsPlanDto>> ListAsync(Guid ownerUserId, bool onlyActive, CancellationToken ct);
    Task<SavingsPlanDto?> GetAsync(Guid id, Guid ownerUserId, CancellationToken ct);
    Task<SavingsPlanDto> CreateAsync(Guid ownerUserId, string name, SavingsPlanType type, decimal? targetAmount, DateTime? targetDate, SavingsPlanInterval? interval, CancellationToken ct);
    Task<SavingsPlanDto?> UpdateAsync(Guid id, Guid ownerUserId, string name, SavingsPlanType type, decimal? targetAmount, DateTime? targetDate, SavingsPlanInterval? interval, Guid? categoryId, CancellationToken ct);
    Task<bool> ArchiveAsync(Guid id, Guid ownerUserId, CancellationToken ct);
    Task<bool> DeleteAsync(Guid id, Guid ownerUserId, CancellationToken ct);
}