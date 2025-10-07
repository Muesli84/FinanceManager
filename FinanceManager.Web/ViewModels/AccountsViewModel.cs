using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace FinanceManager.Web.ViewModels;

public sealed class AccountsViewModel : ViewModelBase
{
    private readonly HttpClient _http;

    public AccountsViewModel(IServiceProvider sp, IHttpClientFactory httpFactory) : base(sp)
    {
        _http = httpFactory.CreateClient("Api");
    }

    public bool Loaded { get; private set; }
    public Guid? FilterBankContactId { get; private set; }

    public List<AccountItem> Accounts { get; } = new();

    public void SetFilter(Guid? bankContactId)
    {
        if (FilterBankContactId != bankContactId)
        {
            FilterBankContactId = bankContactId;
            RaiseStateChanged();
        }
    }

    public override async ValueTask InitializeAsync(CancellationToken ct = default)
    {
        if (!IsAuthenticated)
        {
            RequireAuthentication(null);
            return;
        }
        await LoadAsync(ct);
        Loaded = true;
        RaiseStateChanged();
    }

    public async Task LoadAsync(CancellationToken ct = default)
    {
        if (!IsAuthenticated) { return; }
        var url = "/api/accounts";
        if (FilterBankContactId.HasValue) { url += $"?bankContactId={FilterBankContactId}"; }
        try
        {
            var resp = await _http.GetAsync(url, ct);
            if (resp.IsSuccessStatusCode)
            {
                var list = await resp.Content.ReadFromJsonAsync<List<AccountDto>>(cancellationToken: ct) ?? new();
                Accounts.Clear();
                Accounts.AddRange(list.Select(d => new AccountItem
                {
                    Id = d.Id,
                    Name = d.Name,
                    Type = d.Type.ToString(),
                    Iban = d.Iban,
                    CurrentBalance = d.CurrentBalance
                }));
                RaiseStateChanged();
            }
        }
        catch { }
    }

    public override IReadOnlyList<UiRibbonGroup> GetRibbon(IStringLocalizer localizer)
    {
        var items = new List<UiRibbonItem>
        {
            new UiRibbonItem(localizer["Ribbon_New"], "<svg><use href='/icons/sprite.svg#plus'/></svg>", UiRibbonItemSize.Large, false, "New")
        };
        if (FilterBankContactId.HasValue)
        {
            items.Add(new UiRibbonItem(localizer["Ribbon_ClearFilter"], "<svg><use href='/icons/sprite.svg#clear'/></svg>", UiRibbonItemSize.Small, false, "ClearFilter"));
        }
        return new List<UiRibbonGroup>
        {
            new UiRibbonGroup(localizer["Ribbon_Group_Actions"], items)
        };
    }

    public sealed class AccountItem
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string? Iban { get; set; }
        public decimal CurrentBalance { get; set; }
    }

    public sealed record AccountDto(Guid Id, string Name, AccountType Type, string? Iban, decimal CurrentBalance, Guid BankContactId);
}
