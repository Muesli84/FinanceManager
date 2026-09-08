namespace FinanceManager.Web.Services;

/// <summary>
/// No-op confirmation service used as a fallback when no dialog host is registered.
/// It always returns <c>true</c> so destructive actions can be exercised in headless tests
/// without blocking on a UI dialog.
/// </summary>
internal sealed class NullConfirmationService : IConfirmationService
{
    private NullConfirmationService()
    {
    }

    /// <summary>
    /// Gets the shared instance of the null confirmation service.
    /// </summary>
    /// <value>The shared no-op confirmation service.</value>
    public static IConfirmationService Instance { get; } = new NullConfirmationService();

    /// <inheritdoc />
    public event EventHandler<ConfirmationRequest>? OnShow
    {
        add { }
        remove { }
    }

    /// <inheritdoc />
    public event EventHandler? OnChanged
    {
        add { }
        remove { }
    }

    /// <inheritdoc />
    public ConfirmationRequest? CurrentRequest => null;

    /// <inheritdoc />
    public Task<bool> ConfirmAsync(ConfirmationRequest request, CancellationToken ct = default)
        => Task.FromResult(true);

    /// <inheritdoc />
    public void SetResult(bool confirmed)
    {
    }

    /// <inheritdoc />
    public void Cancel()
    {
    }

    /// <inheritdoc />
    public Task InvalidateCacheAsync()
        => Task.CompletedTask;
}
