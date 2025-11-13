using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FinanceManager.Shared.Dtos;

namespace FinanceManager.Web.Services
{
    public interface IPostingsQueryService
    {
        Task<IReadOnlyList<PostingServiceDto>> GetContactPostingsAsync(Guid contactId, int skip, int take, string? q, DateTime? from, DateTime? to, Guid currentUserId, CancellationToken ct = default);
        Task<IReadOnlyList<PostingServiceDto>> GetAccountPostingsAsync(Guid accountId, int skip, int take, string? q, DateTime? from, DateTime? to, Guid currentUserId, CancellationToken ct = default);
        Task<IReadOnlyList<PostingServiceDto>> GetSavingsPlanPostingsAsync(Guid planId, int skip, int take, string? q, DateTime? from, DateTime? to, Guid currentUserId, CancellationToken ct = default);
        Task<IReadOnlyList<PostingServiceDto>> GetSecurityPostingsAsync(Guid securityId, int skip, int take, DateTime? from, DateTime? to, Guid currentUserId, CancellationToken ct = default);
    }
}
