using FinanceManager.Application.Accounts;

namespace FinanceManager.Tests.TestHelpers;

/// <summary>
/// Placeholder <see cref="IAccountService"/> for constructing components (e.g. <c>StatementDraftService</c>)
/// whose constructor requires an account service but whose test scenario never actually calls into it. Every
/// member throws <see cref="NotImplementedException"/> by design: if a test that plugs this stub in starts
/// exercising account-service behavior, it should fail loudly rather than silently return default data, which
/// signals that the test needs a real mock/fake with configured behavior instead.
/// </summary>
internal sealed class StubAccountService : IAccountService
{
    /// <inheritdoc/>
    public Task<AccountDto> CreateAsync(Guid ownerUserId, string name, AccountType type, string? iban, Guid bankContactId, SavingsPlanExpectation expectation, bool securityProcessingEnabled, bool isCollectionAccount, CancellationToken ct)
        => throw new NotImplementedException();

    /// <inheritdoc/>
    public Task<AccountDto?> UpdateAsync(Guid id, Guid ownerUserId, string name, string? iban, Guid bankContactId, SavingsPlanExpectation expectation, bool securityProcessingEnabled, bool isCollectionAccount, CancellationToken ct)
        => throw new NotImplementedException();

    /// <inheritdoc/>
    public Task<bool> DeleteAsync(Guid id, Guid ownerUserId, CancellationToken ct)
        => throw new NotImplementedException();

    /// <inheritdoc/>
    public Task<IReadOnlyList<AccountDto>> ListAsync(Guid ownerUserId, int skip, int take, Guid? bankContactId, string? q, CancellationToken ct)
        => throw new NotImplementedException();

    /// <inheritdoc/>
    public Task<AccountStatisticsDto> GetStatisticsAsync(Guid ownerUserId, string? q, CancellationToken ct)
        => throw new NotImplementedException();

    /// <inheritdoc/>
    public Task<AccountDto?> GetAsync(Guid id, Guid ownerUserId, CancellationToken ct)
        => throw new NotImplementedException();

    /// <inheritdoc/>
    public AccountDto? Get(Guid id, Guid ownerUserId)
        => throw new NotImplementedException();

    /// <inheritdoc/>
    public Task SetSymbolAttachmentAsync(Guid id, Guid ownerUserId, Guid? attachmentId, CancellationToken ct)
        => throw new NotImplementedException();

    /// <inheritdoc/>
    public Task AddLinkedIbanAsync(Guid accountId, Guid ownerUserId, string iban, CancellationToken ct)
        => throw new NotImplementedException();

    /// <inheritdoc/>
    public Task<bool> RemoveLinkedIbanAsync(Guid accountId, Guid ownerUserId, string iban, CancellationToken ct)
        => throw new NotImplementedException();

    /// <inheritdoc/>
    public Task<IReadOnlyList<string>?> GetLinkedIbansAsync(Guid accountId, Guid ownerUserId, CancellationToken ct)
        => throw new NotImplementedException();
}
