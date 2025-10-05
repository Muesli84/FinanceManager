using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using FinanceManager.Application;

namespace FinanceManager.Web.ViewModels;

public abstract class ViewModelBase : IAsyncDisposable
{
    private readonly IServiceProvider _services;
    private readonly List<IAsyncDisposable> _children = new();
    private readonly List<ViewModelBase> _childViewModels = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly ICurrentUserService _currentUser;

    protected ViewModelBase(IServiceProvider services)
    {
        _services = services;
        _currentUser = services.GetRequiredService<ICurrentUserService>();
    }

    public event EventHandler? StateChanged;
    // Raised when the VM requires authentication; argument may contain a suggested returnUrl
    public event EventHandler<string?>? AuthenticationRequired;

    protected void RaiseStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);

    protected void RequireAuthentication(string? returnUrl = null) => AuthenticationRequired?.Invoke(this, returnUrl);

    public bool IsAuthenticated => _currentUser.IsAuthenticated;

    protected T CreateSubViewModel<T>(Action<T>? configure = null) where T : ViewModelBase
    {
        var vm = ActivatorUtilities.CreateInstance<T>(_services);
        vm.StateChanged += (_, __) => RaiseStateChanged();
        vm.AuthenticationRequired += (_, ret) => AuthenticationRequired?.Invoke(this, ret);
        _children.Add(vm);
        _childViewModels.Add(vm);
        configure?.Invoke(vm);
        return vm;
    }

    protected CancellationToken CancellationToken => _cts.Token;

    // Ausführungshilfe inkl. Render-Trigger
    protected async Task RunAsync(Func<CancellationToken, Task> action, bool raiseAfter = true)
    {
        await action(_cts.Token);
        if (raiseAfter) { RaiseStateChanged(); }
    }

    public virtual ValueTask InitializeAsync(CancellationToken ct = default) => ValueTask.CompletedTask;

    // Ribbon der Sub-ViewModels wird standardmäßig gemerged
    public virtual IReadOnlyList<UiRibbonGroup> GetRibbon(IStringLocalizer localizer)
        => _childViewModels.SelectMany(vm => vm.GetRibbon(localizer)).ToList();

    public virtual async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _cts.Dispose();
        foreach (var c in _children)
        {
            try { await c.DisposeAsync(); } catch { }
        }
        _children.Clear();
        _childViewModels.Clear();
    }
}